// Autoteste do motor Java (o mesmo que roda no app) contra cartoes simulados.
// Roda no PC, sem Android:  python android/build_apk.py --test
import com.sdfake.app.engine.Fmt;
import com.sdfake.app.engine.FullResult;
import com.sdfake.app.engine.FullTest;
import com.sdfake.app.engine.Pattern;
import com.sdfake.app.engine.Store;
import com.sdfake.app.engine.StoreException;
import com.sdfake.app.engine.TestFile;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.HashMap;
import java.util.Map;

public final class SimTest {
    static final int GENUINE = 0, DROP = 1, WRAP = 2, BAD_SPOT = 3, REFUSE = 4;
    static final long MIB = 1L << 20, GIB = 1L << 30;
    static final int BS = Pattern.BLOCK;
    static int failures;

    /** Cartao simulado: guarda so o carimbo (semente, chave) de cada bloco de 1 MiB. */
    static final class SimStore implements Store {
        long declared, real, usedBefore;
        int mode;
        long badSpotStart = -1, badSpotBlocks;
        boolean cleanedUp;
        final Map<Long, long[]> blocks = new HashMap<>();
        final Map<String, Long> start = new HashMap<>();
        long next;
        final long[] tmp = new long[Pattern.LONGS];

        SimStore(long declared, long real, int mode) {
            this.declared = declared;
            this.real = real;
            this.mode = mode;
        }

        @Override public void prepare() { next = usedBefore / BS; }
        @Override public long freeBytes() { return declared - next * BS; }
        @Override public long totalBytes() { return declared; }
        @Override public void cleanup() { cleanedUp = true; }

        @Override public TestFile create(String name) {
            start.put(name, next);
            return new SimFile(next);
        }

        @Override public TestFile open(String name) {
            return new SimFile(start.get(name));
        }

        final class SimFile implements TestFile {
            final long first;
            long count;

            SimFile(long first) { this.first = first; }

            @Override public void write(ByteBuffer buffer) throws IOException {
                long addr = first + count;
                if ((addr + 1) * BS > declared) throw new StoreException("write failed: ENOSPC (No space left on device)") {{ diskFull = true; }};
                if (mode == REFUSE && addr * BS >= real) throw new StoreException("write failed: EIO (I/O error)");
                count++;
                next = addr + 1;
                ByteBuffer b = buffer.duplicate().order(ByteOrder.LITTLE_ENDIAN);
                long[] stamp = { b.getLong(8), b.getLong(0) };   // semente, chave
                buffer.position(buffer.limit());
                if (mode == DROP && addr * BS >= real) return;
                if (mode == WRAP) addr %= (real / BS);
                blocks.put(addr, stamp);
            }

            @Override public void read(long offset, ByteBuffer buffer) {
                long addr = first + offset / BS;
                long phys = mode == WRAP ? addr % (real / BS) : addr;
                long[] stamp = blocks.get(phys);
                buffer.clear();
                if (stamp == null) {
                    java.util.Arrays.fill(tmp, 0L);
                } else {
                    Pattern.fill(tmp, stamp[0], stamp[1]);
                    if (mode == BAD_SPOT && addr >= badSpotStart && addr < badSpotStart + badSpotBlocks) tmp[1024] = 0x0BADBADBADBADBADL;
                }
                buffer.order(ByteOrder.LITTLE_ENDIAN).asLongBuffer().put(tmp);
            }

            @Override public void flush() { }
            @Override public void close() { }
        }
    }

    static void check(String name, boolean ok, FullResult r) {
        System.out.println((ok ? "  [ok]    " : "  [FALHA] ") + name + "  (" + r.title() + ", gravado " + Fmt.bytes(r.writtenBytes)
                + ", ok " + Fmt.bytes(r.okBytes) + ", ruim " + Fmt.bytes(r.badBytes)
                + (r.estimatedRealBytes >= 0 ? ", real~" + Fmt.bytes(r.estimatedRealBytes) : "") + ", " + Fmt.duration(r.elapsedMs) + ")");
        if (!ok) failures++;
    }

    static boolean near(long v, long expected, long tolerance) {
        return Math.abs(v - expected) <= tolerance;
    }

    public static void main(String[] args) {
        System.out.println("== Motor Java (Android) contra cartões simulados ==");

        SimStore s = new SimStore(3000 * MIB, 3000 * MIB, GENUINE);
        FullResult r = new FullTest().run(s);
        check("verdadeiro de 3 GB", r.verdict == FullResult.GENUINE && r.writtenBytes == r.targetBytes && r.badBytes == 0 && s.cleanedUp, r);

        s = new SimStore(128000000000L, 200 * MIB, DROP);
        r = new FullTest().run(s);
        check("falso: anuncia 128 GB, tem 200 MiB (descarta gravações)",
                r.verdict == FullResult.FAKE && near(r.estimatedRealBytes, 200 * MIB, MIB) && r.writtenBytes < 2 * GIB && r.badZero > 0, r);

        s = new SimStore(64 * GIB, 256 * MIB, WRAP);
        r = new FullTest().run(s);
        check("falso: anuncia 64 GB, tem 256 MiB (regrava por cima)",
                r.verdict == FullResult.FAKE && near(r.estimatedRealBytes, 256 * MIB, 2 * MIB) && r.writtenBytes < 4 * GIB && r.badAlias > 0, r);

        s = new SimStore(32 * GIB, 500 * MIB, DROP);
        s.usedBefore = 100 * MIB;
        r = new FullTest().run(s);
        check("falso com 100 MiB já ocupados: real 500 MiB", r.verdict == FullResult.FAKE && near(r.estimatedRealBytes, 500 * MIB, MIB), r);

        s = new SimStore(16 * GIB, 1000 * MIB, REFUSE);
        r = new FullTest().run(s);
        check("falso que dá erro de gravação após 1000 MiB",
                r.verdict == FullResult.FAKE && r.writeError != null && near(r.estimatedRealBytes, 1000 * MIB, MIB), r);

        s = new SimStore(2 * GIB, 2 * GIB, BAD_SPOT);
        s.badSpotStart = 700;
        s.badSpotBlocks = 3;
        r = new FullTest().run(s);
        check("verdadeiro com 3 blocos defeituosos",
                r.verdict == FullResult.DEFECTIVE && r.writtenBytes == r.targetBytes && r.badBytes == 3 * 4096 && r.badOther == 3, r);

        s = new SimStore(2 * GIB, 2 * GIB, GENUINE);
        final FullTest ft = new FullTest();
        ft.reportIntervalMs = 0;
        final int[] calls = { 0 };
        ft.listener = new FullTest.Listener() {
            @Override public void onProgress(String phase, double fraction, String detail) { if (++calls[0] == 300) ft.cancelRequested = true; }
            @Override public void onLog(String line) { }
        };
        r = ft.run(s);
        check("cancelamento", r.verdict == FullResult.INCOMPLETE && r.cancelled && s.cleanedUp, r);

        System.out.println();
        System.out.println(failures == 0 ? "TODOS OS TESTES PASSARAM" : failures + " TESTE(S) FALHARAM");
        System.exit(failures == 0 ? 0 : 1);
    }
}
