package com.sdfake.app.engine;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.LongBuffer;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.Random;

/**
 * Teste Completo: preenche o espaco livre com arquivos de teste e confere tudo.
 * Mesmo algoritmo da versao para Windows (pc/src/FullTest.cs). Nao depende do Android.
 */
public final class FullTest {

    public interface Listener {
        void onProgress(String phase, double fraction, String detail);

        void onLog(String line);
    }

    public volatile boolean cancelRequested;
    public Listener listener;
    /** 0 = testar todo o espaco livre. */
    public long limitBytes;
    public int firstFileMiB = 16;
    public int maxFileMiB = 1024;
    public int reportIntervalMs = 200;
    /**
     * Protecao contra cache do sistema (Android): se o teste gravou menos que isto, um resultado
     * "verdadeiro" so vale se as leituras comprovadamente vieram do dispositivo. 0 = desligado.
     */
    public long cacheRiskBytes;
    /** Leitura mais rapida que isto (MB/s) nao pode ter vindo de um cartao ou pendrive. */
    public double maxPlausibleReadMBs = 2000;

    private static final int BS = Pattern.BLOCK;
    private static final int PIECES_PER_BLOCK = BS / Pattern.PIECE;
    private static final int KIND_ZERO = 1, KIND_ALIAS = 2, KIND_OTHER = 3, KIND_UNREADABLE = 4;

    private static final class FileRec {
        String name;
        int index;
        int blocks;
    }

    private static final class WriteOutcome {
        boolean diskFull, cancelled;
        String error;
    }

    private static final class Tally {
        long ok, bad, firstBad = -1;
        int zero, alias, other, unreadable;
        boolean cancelled;
    }

    /** Erro que encerra o teste com uma mensagem para o usuario. */
    private static final class Abort extends RuntimeException {
        Abort(String message) { super(message); }
    }

    /** Velocidade media dos ultimos segundos. */
    static final class RateMeter {
        private final long start = System.nanoTime();
        private final ArrayList<long[]> window = new ArrayList<>();
        private long total;

        void add(long bytes) {
            total += bytes;
            long now = System.nanoTime();
            window.add(new long[] { now, total });
            while (window.size() > 2 && now - window.get(0)[0] > 8_000_000_000L) window.remove(0);
        }

        double average() {
            double t = (System.nanoTime() - start) / 1e9;
            return t < 0.05 ? 0 : total / t / 1e6;
        }

        double recent() {
            if (window.size() < 2) return average();
            long[] first = window.get(0);
            double dt = (System.nanoTime() - first[0]) / 1e9;
            if (dt < 0.2) return average();
            return (total - first[1]) / dt / 1e6;
        }
    }

    private long seed;
    private final long[] expected = new long[Pattern.LONGS];
    private final long[] actual = new long[Pattern.LONGS];
    private ByteBuffer io;
    private LongBuffer view;
    private final Random rnd = new Random();
    private long lastReport;
    private int lastKind;
    private long readNanos, readBytes;

    private static long key(int fileIndex, int block) {
        return ((long) fileIndex << 32) | (block & 0xFFFFFFFFL);
    }

    public static String fileName(int index) {
        return String.format(java.util.Locale.ROOT, "SDF%05d.bin", index);
    }

    private void log(String s) {
        if (listener != null) listener.onLog(s);
    }

    private void report(String phase, long done, long total, RateMeter meter, boolean force) {
        if (listener == null) return;
        long now = System.currentTimeMillis();
        if (!force && now - lastReport < reportIntervalMs) return;
        lastReport = now;
        double fraction = total > 0 ? Math.min(1.0, (double) done / total) : 0;
        double speed = meter != null ? meter.recent() : 0;
        String eta = meter != null ? Fmt.eta(total - done, speed) : "";
        String detail = Fmt.bytes(done) + " de " + Fmt.bytes(total)
                + (speed > 0 ? "  ·  " + Fmt.speed(speed) : "")
                + (eta.length() > 0 ? "  ·  " + eta : "");
        listener.onProgress(phase, fraction, detail);
    }

    public FullResult run(Store store) {
        FullResult r = new FullResult();
        long started = System.currentTimeMillis();
        List<FileRec> files = new ArrayList<>();
        boolean prepared = false;
        try {
            io = ByteBuffer.allocateDirect(BS).order(ByteOrder.LITTLE_ENDIAN);
            view = io.asLongBuffer();
            store.prepare();
            prepared = true;

            r.totalBytes = store.totalBytes();
            r.freeBefore = store.freeBytes();
            long usedBefore = Math.max(0, r.totalBytes - r.freeBefore);
            long target = r.freeBefore - 4L * BS;
            if (limitBytes > 0 && target > limitBytes) target = limitBytes;
            target -= target % BS;
            if (target < 8L * BS)
                throw new Abort("Há pouco espaço livre no dispositivo para testar. Libere espaço (ou formate o cartão) e tente de novo.");
            r.targetBytes = target;
            seed = Pattern.newSeed();
            log("Espaço livre a testar: " + Fmt.bytes(target) + " (tamanho anunciado: " + Fmt.bytes(r.totalBytes) + ")");

            // ---- Fase 1: gravar, conferindo por amostragem depois de cada arquivo ----
            long written = 0;
            int index = 0;
            long nextSize = (long) firstFileMiB * BS;
            boolean tolerant = false;
            boolean stop = false;
            RateMeter wm = new RateMeter();

            while (written < target && !stop) {
                if (cancelRequested) { r.cancelled = true; break; }
                long size = Math.min(nextSize, target - written);
                FileRec f = new FileRec();
                f.name = fileName(index);
                f.index = index;
                WriteOutcome o = writeOne(store, f, (int) (size / BS), written, target, wm);
                if (f.blocks > 0) files.add(f);
                written += (long) f.blocks * BS;
                if (o.cancelled) { r.cancelled = true; break; }
                if (o.diskFull) stop = true;
                if (o.error != null) {
                    r.writeError = o.error;
                    log("Erro de gravação após " + Fmt.bytes(written) + ": " + o.error);
                    stop = true;
                }

                if (f.blocks > 0 && !stop) {
                    int[] sb = quickCheck(store, files);
                    int samples = sb[0], bad = sb[1];
                    if (bad > 0) {
                        log("Conferência rápida encontrou dados incorretos após " + Fmt.bytes(written) + " gravados.");
                        if (tolerant) {
                            if (bad * 2 >= samples) { r.stoppedEarly = true; stop = true; }
                        } else {
                            Tally t = verifyAll(store, files, "Conferindo a falha encontrada", null);
                            if (t.cancelled) { r.cancelled = true; break; }
                            if (t.bad >= 0.02 * written) {
                                r.stoppedEarly = true;
                                stop = true;
                                log("Falha em grande escala confirmada: não adianta continuar gravando.");
                            } else {
                                tolerant = true;
                            }
                        }
                    }
                }
                if (nextSize < (long) maxFileMiB * BS) nextSize *= 2;
                index++;
            }
            r.writtenBytes = written;
            r.writeMBs = wm.average();

            if (written == 0 && !r.cancelled)
                throw new Abort("Não foi possível gravar no dispositivo" + (r.writeError != null ? ": " + r.writeError : "."));

            // ---- Fase 2: ler tudo de volta e conferir ----
            if (!r.cancelled) {
                log("Gravação concluída (" + Fmt.bytes(written) + "). Verificando tudo...");
                RateMeter rm = new RateMeter();
                readNanos = 0;
                readBytes = 0;
                Tally t = verifyAll(store, files, "Verificando (etapa 2 de 2)", rm);
                r.readMBs = rm.average();
                r.readIoMBs = readNanos > 0 ? readBytes / (readNanos / 1e9) / 1e6 : 0;
                r.okBytes = t.ok;
                r.badBytes = t.bad;
                r.firstBadOffset = t.firstBad;
                r.badZero = t.zero;
                r.badAlias = t.alias;
                r.badOther = t.other;
                r.badUnreadable = t.unreadable;
                if (t.cancelled) r.cancelled = true; else r.verified = true;
            }

            if (r.cancelled) {
                r.verdict = FullResult.INCOMPLETE;
            } else if (r.badBytes == 0 && r.writeError == null) {
                boolean fromMemory = !store.readsBypassCache() || r.readIoMBs > maxPlausibleReadMBs;
                r.verdict = (written < cacheRiskBytes && fromMemory) ? FullResult.UNCONFIRMED : FullResult.GENUINE;
            } else {
                double frac = (double) r.badBytes / written;
                boolean refused = r.writeError != null && written < target * 0.9;
                r.verdict = (r.stoppedEarly || frac >= 0.02 || refused) ? FullResult.FAKE : FullResult.DEFECTIVE;
                r.estimatedRealBytes = usedBefore + r.okBytes;
            }
        } catch (Abort e) {
            r.verdict = FullResult.ERROR;
            r.fatalError = e.getMessage();
        } catch (IOException e) {
            StoreException s = StoreException.wrap("Erro no dispositivo", e);
            r.verdict = FullResult.ERROR;
            r.fatalError = s.deviceGone
                    ? "O dispositivo parou de responder ou foi desconectado durante o teste (" + s.getMessage() + "). "
                    + "Se você não removeu o cartão, isso é comum em cartões falsos ou com defeito."
                    : s.getMessage();
        } catch (RuntimeException e) {
            r.verdict = FullResult.ERROR;
            r.fatalError = "Erro inesperado: " + e;
        } finally {
            if (prepared) {
                if (listener != null) listener.onProgress("Apagando os arquivos de teste", 1.0, "");
                store.cleanup();
            }
            r.elapsedMs = System.currentTimeMillis() - started;
        }
        return r;
    }

    private void classify(IOException e, WriteOutcome o) throws IOException {
        StoreException s = StoreException.wrap("Falha ao gravar", e);
        if (s.deviceGone) throw s;
        if (s.readOnly)
            throw new Abort("O dispositivo está protegido contra gravação. Destrave a chavinha lateral do cartão/adaptador (LOCK) e tente de novo.");
        if (s.diskFull) o.diskFull = true; else o.error = s.getMessage();
    }

    private WriteOutcome writeOne(Store store, FileRec f, int blocks, long writtenBefore, long target, RateMeter wm) throws IOException {
        WriteOutcome o = new WriteOutcome();
        TestFile file = null;
        try {
            try {
                file = store.create(f.name);
            } catch (IOException e) {
                classify(e, o);
                return o;
            }
            for (int b = 0; b < blocks; b++) {
                if (cancelRequested) { o.cancelled = true; break; }
                Pattern.fill(expected, seed, key(f.index, b));
                view.clear();
                view.put(expected);
                io.clear();
                try {
                    file.write(io);
                } catch (IOException e) {
                    classify(e, o);
                    break;
                }
                f.blocks = b + 1;
                wm.add(BS);
                report("Gravando (etapa 1 de 2)", writtenBefore + (long) f.blocks * BS, target, wm, false);
            }
            try {
                file.flush();
            } catch (IOException e) {
                if (o.error == null && !o.diskFull) classify(e, o);
            }
        } finally {
            closeQuietly(file);
        }
        return o;
    }

    private static void closeQuietly(TestFile file) {
        if (file == null) return;
        try { file.close(); } catch (IOException | RuntimeException ignored) { }
    }

    /** Le um bloco e compara com o esperado. Retorna quantos pedacos de 4 KiB estao errados; o tipo fica em lastKind. */
    private int checkBlock(TestFile file, int fileIndex, int block) throws IOException {
        lastKind = 0;
        try {
            io.clear();
            long t0 = System.nanoTime();
            file.read((long) block * BS, io);
            readNanos += System.nanoTime() - t0;
            readBytes += BS;
        } catch (IOException e) {
            if (StoreException.wrap("Falha ao ler", e).deviceGone) throw e;
            lastKind = KIND_UNREADABLE;
            return PIECES_PER_BLOCK;
        }
        view.clear();
        view.get(actual);
        long k = key(fileIndex, block);
        Pattern.fill(expected, seed, k);
        if (Arrays.equals(expected, actual)) return 0;
        int bad = Pattern.badPieces(expected, actual);
        if (Pattern.isBlank(actual)) lastKind = KIND_ZERO;
        else if (actual[1] == seed && actual[0] != k) lastKind = KIND_ALIAS;
        else lastKind = KIND_OTHER;
        return bad;
    }

    private void checkSamples(Store store, FileRec f, List<Integer> blocks, int[] sb) throws IOException {
        TestFile file = null;
        try {
            try {
                file = store.open(f.name);
            } catch (IOException e) {
                if (StoreException.wrap("Falha ao abrir", e).deviceGone) throw e;
                sb[0] += blocks.size();
                sb[1] += blocks.size();
                return;
            }
            for (int b : blocks) {
                sb[0]++;
                if (checkBlock(file, f.index, b) != 0) sb[1]++;
            }
        } finally {
            closeQuietly(file);
        }
    }

    /**
     * Amostra o arquivo recem-gravado (detecta gravacoes descartadas) e arquivos antigos
     * (detecta cartoes que sobrescrevem o comeco quando a capacidade real acaba).
     * Retorna {amostras, ruins}.
     */
    private int[] quickCheck(Store store, List<FileRec> files) throws IOException {
        int[] sb = new int[2];
        FileRec cur = files.get(files.size() - 1);
        List<Integer> blocks = new ArrayList<>();
        blocks.add(0);
        if (cur.blocks > 1) blocks.add(cur.blocks - 1);
        for (int i = 0; i < 6 && cur.blocks > 2; i++) {
            int b = 1 + rnd.nextInt(cur.blocks - 2);
            if (!blocks.contains(b)) blocks.add(b);
        }
        checkSamples(store, cur, blocks, sb);

        if (files.size() > 1) {
            List<Integer> first = new ArrayList<>();
            first.add(0);
            checkSamples(store, files.get(0), first, sb);
            for (int i = 0; i < 3; i++) {
                FileRec old = files.get(rnd.nextInt(files.size() - 1));
                List<Integer> one = new ArrayList<>();
                one.add(rnd.nextInt(old.blocks));
                checkSamples(store, old, one, sb);
            }
        }
        return sb;
    }

    private Tally verifyAll(Store store, List<FileRec> files, String phase, RateMeter rm) throws IOException {
        Tally t = new Tally();
        RateMeter meter = rm != null ? rm : new RateMeter();
        long total = 0;
        for (FileRec f : files) total += (long) f.blocks * BS;
        long done = 0;
        int consecutiveIo = 0;
        boolean giveUp = false;

        for (FileRec f : files) {
            TestFile file = null;
            try {
                if (!giveUp) {
                    try {
                        file = store.open(f.name);
                    } catch (IOException e) {
                        if (StoreException.wrap("Falha ao abrir", e).deviceGone) throw e;
                        file = null;
                    }
                }
                for (int b = 0; b < f.blocks; b++) {
                    if (cancelRequested) { t.cancelled = true; return t; }
                    int kind;
                    int badPieces;
                    if (file == null) {
                        kind = KIND_UNREADABLE;
                        badPieces = PIECES_PER_BLOCK;
                    } else {
                        badPieces = checkBlock(file, f.index, b);
                        kind = lastKind;
                    }

                    if (badPieces == 0) {
                        t.ok += BS;
                        consecutiveIo = 0;
                    } else {
                        long badBytes = (long) badPieces * Pattern.PIECE;
                        t.bad += badBytes;
                        t.ok += BS - badBytes;
                        if (t.firstBad < 0) t.firstBad = done;
                        if (kind == KIND_ZERO) t.zero++;
                        else if (kind == KIND_ALIAS) t.alias++;
                        else if (kind == KIND_OTHER) t.other++;
                        else t.unreadable++;

                        if (kind == KIND_UNREADABLE && file != null) {
                            // Leituras com erro costumam ser lentas: depois de varias seguidas, desiste do resto.
                            if (++consecutiveIo >= 16) {
                                giveUp = true;
                                closeQuietly(file);
                                file = null;
                            }
                        } else if (kind != KIND_UNREADABLE) {
                            consecutiveIo = 0;
                        }
                    }
                    done += BS;
                    meter.add(BS);
                    report(phase, done, total, meter, false);
                }
            } finally {
                closeQuietly(file);
            }
        }
        report(phase, total, total, meter, true);
        return t;
    }
}
