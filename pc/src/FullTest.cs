// SdFake - Teste Completo: preenche o espaco livre com arquivos de teste e confere tudo.
// Nao apaga arquivos do usuario; funciona sem administrador.
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace SdFake
{
    public class FullResult
    {
        public Verdict Verdict;
        /// <summary>Tamanho que o dispositivo fisico anuncia (0 = desconhecido). Pode ser maior que o volume formatado.</summary>
        public long DeviceBytes;
        public long TotalBytes;
        public long FreeBefore;
        public long TargetBytes;
        public long WrittenBytes;
        public long OkBytes;
        public long BadBytes;
        public long FirstBadOffset = -1;
        public int BadZero, BadAlias, BadOther, BadUnreadable;   // em blocos de 1 MiB
        public bool StoppedEarly;
        public bool Cancelled;
        public bool Verified;
        public string WriteError;
        public string FatalError;
        public double WriteMBs, ReadMBs;
        public long EstimatedRealBytes = -1;
        public TimeSpan Elapsed;
    }

    public class FullTest
    {
        public volatile bool CancelRequested;
        public Action<Progress> OnProgress;
        public Action<string> OnLog;
        /// <summary>0 = testar todo o espaco livre.</summary>
        public long LimitBytes;
        /// <summary>
        /// Tamanho anunciado pelo dispositivo fisico (0 = desconhecido). Este teste so alcanca o
        /// volume formatado; se o volume for bem menor que o dispositivo, "sem erros" nao prova
        /// que a capacidade anunciada existe (caso tipico: cartao falso reformatado no tamanho real).
        /// </summary>
        public long DeviceBytes;
        public int FirstFileMiB = 16;
        public int MaxFileMiB = 1024;
        public int ReportIntervalMs = 200;

        const int BS = Pattern.BlockSize;
        const int PiecesPerBlock = BS / Pattern.Piece;
        const int KindZero = 1, KindAlias = 2, KindOther = 3, KindUnreadable = 4;

        class FileRec { public string Name; public int Index; public int Blocks; }
        class WriteOutcome { public bool DiskFull, Cancelled; public string Error; }
        class Tally
        {
            public long Ok, Bad, FirstBad = -1;
            public int Zero, Alias, Other, Unreadable;
            public bool Cancelled;
        }

        ulong seed;
        AlignedBuffer bufA, bufB;
        readonly Random rnd = new Random();
        readonly Stopwatch throttle = Stopwatch.StartNew();
        long lastReportMs = -1000;

        static ulong Key(int fileIndex, int block)
        {
            return ((ulong)(uint)fileIndex << 32) | (uint)block;
        }

        public static string FileName(int index)
        {
            return "SDF" + index.ToString("D5") + ".bin";
        }

        void Log(string s) { if (OnLog != null) OnLog(s); }

        void Report(string phase, long done, long total, RateMeter meter, bool force)
        {
            if (OnProgress == null) return;
            long now = throttle.ElapsedMilliseconds;
            if (!force && now - lastReportMs < ReportIntervalMs) return;
            lastReportMs = now;
            Progress p = new Progress();
            p.Phase = phase;
            p.Fraction = total > 0 ? Math.Min(1.0, (double)done / total) : 0;
            double speed = meter != null ? meter.Recent : 0;
            string eta = meter != null ? Fmt.Eta(total - done, speed) : "";
            p.Detail = Fmt.Bytes(done) + " de " + Fmt.Bytes(total)
                     + (speed > 0 ? "  ·  " + Fmt.Speed(speed) : "")
                     + (eta.Length > 0 ? "  ·  " + eta : "");
            OnProgress(p);
        }

        public FullResult Run(ITestStore store)
        {
            FullResult r = new FullResult();
            Stopwatch clock = Stopwatch.StartNew();
            List<FileRec> files = new List<FileRec>();
            bool prepared = false;
            try
            {
                bufA = new AlignedBuffer(BS);
                bufB = new AlignedBuffer(BS);
                store.Prepare();
                prepared = true;

                r.TotalBytes = store.TotalBytes();
                r.FreeBefore = store.FreeBytes();
                long usedBefore = Math.Max(0, r.TotalBytes - r.FreeBefore);
                long target = r.FreeBefore - 4L * BS;
                if (LimitBytes > 0 && target > LimitBytes) target = LimitBytes;
                target -= target % BS;
                if (target < 8L * BS)
                    throw new TestAbortException("Há pouco espaço livre no dispositivo para testar. Libere espaço (ou formate o cartão) e tente de novo.");
                r.TargetBytes = target;
                seed = Pattern.NewSeed();
                r.DeviceBytes = DeviceBytes;
                if (CoversDevice(r))
                    Log("Espaço livre a testar: " + Fmt.Bytes(target) + " (tamanho anunciado: " + Fmt.Bytes(r.TotalBytes) + ")");
                else
                    Log("Espaço livre a testar: " + Fmt.Bytes(target) + ". ATENÇÃO: o dispositivo anuncia " + Fmt.Bytes(r.DeviceBytes)
                        + ", mas está formatado com apenas " + Fmt.Bytes(r.TotalBytes) + "; este teste só alcança a parte formatada.");

                // ---- Fase 1: gravar, conferindo por amostragem depois de cada arquivo ----
                long written = 0;
                int index = 0;
                long nextSize = (long)FirstFileMiB * BS;
                bool tolerant = false;
                bool stop = false;
                RateMeter wm = new RateMeter();

                while (written < target && !stop)
                {
                    if (CancelRequested) { r.Cancelled = true; break; }
                    long size = Math.Min(nextSize, target - written);
                    FileRec f = new FileRec();
                    f.Name = FileName(index);
                    f.Index = index;
                    WriteOutcome o = WriteOne(store, f, (int)(size / BS), written, target, wm);
                    if (f.Blocks > 0) files.Add(f);
                    written += (long)f.Blocks * BS;
                    if (o.Cancelled) { r.Cancelled = true; break; }
                    if (o.DiskFull) stop = true;
                    if (o.Error != null)
                    {
                        r.WriteError = o.Error;
                        Log("Erro de gravação após " + Fmt.Bytes(written) + ": " + o.Error);
                        stop = true;
                    }

                    if (f.Blocks > 0 && !stop)
                    {
                        int samples, bad;
                        QuickCheck(store, files, out samples, out bad);
                        if (bad > 0)
                        {
                            Log("Conferência rápida encontrou dados incorretos após " + Fmt.Bytes(written) + " gravados.");
                            if (tolerant)
                            {
                                if (bad * 2 >= samples) { r.StoppedEarly = true; stop = true; }
                            }
                            else
                            {
                                Tally t = VerifyAll(store, files, "Conferindo a falha encontrada", null);
                                if (t.Cancelled) { r.Cancelled = true; break; }
                                if (t.Bad >= 0.02 * written)
                                {
                                    r.StoppedEarly = true;
                                    stop = true;
                                    Log("Falha em grande escala confirmada: não adianta continuar gravando.");
                                }
                                else
                                {
                                    tolerant = true;
                                }
                            }
                        }
                    }
                    if (nextSize < (long)MaxFileMiB * BS) nextSize *= 2;
                    index++;
                }
                r.WrittenBytes = written;
                r.WriteMBs = wm.Average;

                if (written == 0 && !r.Cancelled)
                    throw new TestAbortException("Não foi possível gravar no dispositivo"
                        + (r.WriteError != null ? ": " + r.WriteError : "."));

                // ---- Fase 2: ler tudo de volta e conferir ----
                if (!r.Cancelled)
                {
                    Log("Gravação concluída (" + Fmt.Bytes(written) + "). Verificando tudo...");
                    RateMeter rm = new RateMeter();
                    Tally t = VerifyAll(store, files, "Verificando (etapa 2 de 2)", rm);
                    r.ReadMBs = rm.Average;
                    r.OkBytes = t.Ok;
                    r.BadBytes = t.Bad;
                    r.FirstBadOffset = t.FirstBad;
                    r.BadZero = t.Zero;
                    r.BadAlias = t.Alias;
                    r.BadOther = t.Other;
                    r.BadUnreadable = t.Unreadable;
                    if (t.Cancelled) r.Cancelled = true; else r.Verified = true;
                }

                if (r.Cancelled)
                {
                    r.Verdict = Verdict.Incomplete;
                }
                else if (r.BadBytes == 0 && r.WriteError == null)
                {
                    r.Verdict = CoversDevice(r) ? Verdict.Genuine : Verdict.Partial;
                }
                else
                {
                    double frac = (double)r.BadBytes / written;
                    bool refused = r.WriteError != null && written < target * 0.9;
                    r.Verdict = (r.StoppedEarly || frac >= 0.02 || refused) ? Verdict.Fake : Verdict.Defective;
                    r.EstimatedRealBytes = usedBefore + r.OkBytes;
                }
            }
            catch (TestAbortException e)
            {
                r.Verdict = Verdict.Error;
                r.FatalError = e.Message;
            }
            catch (TestIOException e)
            {
                r.Verdict = Verdict.Error;
                r.FatalError = e.DeviceGone
                    ? "O dispositivo parou de responder ou foi desconectado durante o teste (" + e.Message + "). "
                      + "Se você não removeu o cartão, isso é comum em cartões falsos ou com defeito."
                    : e.Message;
            }
            finally
            {
                if (prepared)
                {
                    Report("Apagando os arquivos de teste", 1, 1, null, true);
                    try { store.Cleanup(); } catch (Exception) { }
                }
                if (bufA != null) bufA.Dispose();
                if (bufB != null) bufB.Dispose();
                r.Elapsed = clock.Elapsed;
            }
            return r;
        }

        /// <summary>false quando o volume formatado e bem menor que o dispositivo anuncia.</summary>
        public static bool CoversDevice(FullResult r)
        {
            return r.DeviceBytes <= 0 || r.TotalBytes >= r.DeviceBytes * 0.9;
        }

        void Classify(TestIOException e, WriteOutcome o)
        {
            if (e.DeviceGone) throw e;
            if (e.WriteProtected)
                throw new TestAbortException("O dispositivo está protegido contra gravação. Destrave a chavinha lateral do cartão/adaptador (LOCK) e tente de novo.");
            if (e.DiskFull) o.DiskFull = true; else o.Error = e.Message;
        }

        WriteOutcome WriteOne(ITestStore store, FileRec f, int blocks, long writtenBefore, long target, RateMeter wm)
        {
            WriteOutcome o = new WriteOutcome();
            ITestFile file = null;
            try
            {
                try { file = store.Create(f.Name); }
                catch (TestIOException e) { Classify(e, o); return o; }

                for (int b = 0; b < blocks; b++)
                {
                    if (CancelRequested) { o.Cancelled = true; break; }
                    Pattern.Fill(bufA.Ptr, BS, seed, Key(f.Index, b));
                    try { file.Write(bufA.Ptr, BS); }
                    catch (TestIOException e) { Classify(e, o); break; }
                    f.Blocks = b + 1;
                    wm.Add(BS);
                    Report("Gravando (etapa 1 de 2)", writtenBefore + (long)f.Blocks * BS, target, wm, false);
                }
                try { file.Flush(); }
                catch (TestIOException e)
                {
                    if (o.Error == null && !o.DiskFull) Classify(e, o);
                }
            }
            finally
            {
                if (file != null) { try { file.Dispose(); } catch (Exception) { } }
            }
            return o;
        }

        /// <summary>Le um bloco e compara com o esperado. Retorna quantos pedacos de 4 KiB estao errados.</summary>
        int CheckBlock(ITestFile file, int fileIndex, int block, out int kind)
        {
            kind = 0;
            try { file.Read((long)block * BS, bufB.Ptr, BS); }
            catch (TestIOException e)
            {
                if (e.DeviceGone) throw;
                kind = KindUnreadable;
                return PiecesPerBlock;
            }
            ulong key = Key(fileIndex, block);
            Pattern.Fill(bufA.Ptr, BS, seed, key);
            if (Pattern.Equal(bufA.Ptr, bufB.Ptr, BS)) return 0;
            int bad = Pattern.BadPieces(bufA.Ptr, bufB.Ptr, BS);
            if (Pattern.IsBlank(bufB.Ptr, BS)) kind = KindZero;
            else if (Pattern.StampSeed(bufB.Ptr) == seed && Pattern.StampKey(bufB.Ptr) != key) kind = KindAlias;
            else kind = KindOther;
            return bad;
        }

        void CheckSamples(ITestStore store, FileRec f, List<int> blocks, ref int samples, ref int bad)
        {
            ITestFile file = null;
            try
            {
                try { file = store.Open(f.Name); }
                catch (TestIOException e)
                {
                    if (e.DeviceGone) throw;
                    samples += blocks.Count;
                    bad += blocks.Count;
                    return;
                }
                foreach (int b in blocks)
                {
                    int kind;
                    samples++;
                    if (CheckBlock(file, f.Index, b, out kind) != 0) bad++;
                }
            }
            finally
            {
                if (file != null) { try { file.Dispose(); } catch (Exception) { } }
            }
        }

        /// <summary>
        /// Amostra o arquivo recem-gravado (detecta gravacoes descartadas) e arquivos antigos
        /// (detecta cartoes que sobrescrevem o comeco quando a capacidade real acaba).
        /// </summary>
        void QuickCheck(ITestStore store, List<FileRec> files, out int samples, out int bad)
        {
            samples = 0;
            bad = 0;
            FileRec cur = files[files.Count - 1];
            List<int> blocks = new List<int>();
            blocks.Add(0);
            if (cur.Blocks > 1) blocks.Add(cur.Blocks - 1);
            for (int i = 0; i < 6 && cur.Blocks > 2; i++)
            {
                int b = 1 + rnd.Next(cur.Blocks - 2);
                if (!blocks.Contains(b)) blocks.Add(b);
            }
            CheckSamples(store, cur, blocks, ref samples, ref bad);

            if (files.Count > 1)
            {
                List<int> first = new List<int>();
                first.Add(0);
                CheckSamples(store, files[0], first, ref samples, ref bad);
                for (int i = 0; i < 3; i++)
                {
                    FileRec old = files[rnd.Next(files.Count - 1)];
                    List<int> one = new List<int>();
                    one.Add(rnd.Next(old.Blocks));
                    CheckSamples(store, old, one, ref samples, ref bad);
                }
            }
        }

        Tally VerifyAll(ITestStore store, List<FileRec> files, string phase, RateMeter rm)
        {
            Tally t = new Tally();
            RateMeter meter = rm != null ? rm : new RateMeter();
            long total = 0;
            foreach (FileRec f in files) total += (long)f.Blocks * BS;
            long done = 0;
            int consecutiveIo = 0;
            bool giveUp = false;

            foreach (FileRec f in files)
            {
                ITestFile file = null;
                try
                {
                    if (!giveUp)
                    {
                        try { file = store.Open(f.Name); }
                        catch (TestIOException e)
                        {
                            if (e.DeviceGone) throw;
                            file = null;
                        }
                    }
                    for (int b = 0; b < f.Blocks; b++)
                    {
                        if (CancelRequested) { t.Cancelled = true; return t; }
                        int kind;
                        int badPieces;
                        if (file == null) { kind = KindUnreadable; badPieces = PiecesPerBlock; }
                        else badPieces = CheckBlock(file, f.Index, b, out kind);

                        if (badPieces == 0)
                        {
                            t.Ok += BS;
                            consecutiveIo = 0;
                        }
                        else
                        {
                            long badBytes = (long)badPieces * Pattern.Piece;
                            t.Bad += badBytes;
                            t.Ok += BS - badBytes;
                            if (t.FirstBad < 0) t.FirstBad = done;
                            if (kind == KindZero) t.Zero++;
                            else if (kind == KindAlias) t.Alias++;
                            else if (kind == KindOther) t.Other++;
                            else t.Unreadable++;

                            if (kind == KindUnreadable && file != null)
                            {
                                // Leituras com erro costumam ser lentas: depois de varias seguidas, desiste do resto.
                                if (++consecutiveIo >= 16)
                                {
                                    giveUp = true;
                                    try { file.Dispose(); } catch (Exception) { }
                                    file = null;
                                }
                            }
                            else if (kind != KindUnreadable) consecutiveIo = 0;
                        }
                        done += BS;
                        meter.Add(BS);
                        Report(phase, done, total, meter, false);
                    }
                }
                finally
                {
                    if (file != null) { try { file.Dispose(); } catch (Exception) { } }
                }
            }
            Report(phase, total, total, meter, true);
            return t;
        }

        // ------------------------------------------------------------------ relatorio

        public static ReportText Describe(FullResult r)
        {
            ReportText t = new ReportText();
            t.Verdict = r.Verdict;
            long announced = Math.Max(r.DeviceBytes, r.TotalBytes);
            switch (r.Verdict)
            {
                case Verdict.Genuine:
                    t.Title = "VERDADEIRO";
                    t.Summary = "Capacidade real confirmada: " + Fmt.Bytes(r.WrittenBytes)
                              + " gravados e conferidos sem nenhum erro.";
                    break;
                case Verdict.Partial:
                    t.Title = "NÃO CONFIRMADO";
                    t.Summary = "O dispositivo anuncia " + Fmt.Bytes(r.DeviceBytes) + ", mas está formatado com apenas " + Fmt.Bytes(r.TotalBytes)
                              + ". Essa parte funciona, só que o restante não foi testado: rode o Teste Rápido, que examina o dispositivo inteiro.";
                    break;
                case Verdict.Fake:
                    t.Title = "FALSO";
                    if (r.BadBytes == 0 && r.WriteError != null)
                        t.Summary = "O dispositivo passou a recusar gravações depois de " + Fmt.Bytes(r.WrittenBytes)
                                  + ". Capacidade utilizável aproximada: " + Fmt.Bytes(r.EstimatedRealBytes)
                                  + " (anunciado: " + Fmt.Bytes(announced) + ").";
                    else
                        t.Summary = "Capacidade real aproximada: " + Fmt.Bytes(r.EstimatedRealBytes)
                                  + "  —  anunciado: " + Fmt.Bytes(announced) + ".";
                    break;
                case Verdict.Defective:
                    t.Title = "COM DEFEITO";
                    t.Summary = "A capacidade parece verdadeira, mas " + Fmt.Bytes(r.BadBytes)
                              + " de dados voltaram corrompidos. Não guarde arquivos importantes neste dispositivo.";
                    break;
                case Verdict.Incomplete:
                    t.Title = "TESTE INCOMPLETO";
                    t.Summary = r.BadBytes > 0
                        ? "Teste cancelado, mas já havia " + Fmt.Bytes(r.BadBytes) + " de dados corrompidos: forte sinal de dispositivo falso ou com defeito."
                        : "Teste cancelado antes do fim; não dá para concluir nada. Rode de novo até terminar.";
                    break;
                default:
                    t.Title = "ERRO";
                    t.Summary = r.FatalError != null ? r.FatalError : "Erro inesperado.";
                    break;
            }

            if (r.DeviceBytes > 0 && !CoversDevice(r))
            {
                t.Lines.Add("Capacidade anunciada pelo dispositivo: " + Fmt.Bytes(r.DeviceBytes));
                t.Lines.Add("Tamanho formatado (o que este teste alcança): " + Fmt.Bytes(r.TotalBytes));
                t.Lines.Add("Um cartão formatado com muito menos do que anuncia costuma ser um cartão falso que alguém reformatou no tamanho real.");
            }
            else if (r.TotalBytes > 0) t.Lines.Add("Capacidade anunciada (tamanho do volume): " + Fmt.Bytes(r.TotalBytes));
            if (r.TargetBytes > 0) t.Lines.Add("Espaço livre testado: " + Fmt.Bytes(r.WrittenBytes) + " de " + Fmt.Bytes(r.TargetBytes));
            if (r.Verified || r.BadBytes > 0)
            {
                t.Lines.Add("Dados conferidos e corretos: " + Fmt.Bytes(r.OkBytes));
                t.Lines.Add("Dados perdidos ou corrompidos: " + Fmt.Bytes(r.BadBytes));
            }
            if (r.FirstBadOffset >= 0) t.Lines.Add("Primeiro erro apareceu depois de " + Fmt.Bytes(r.FirstBadOffset) + " de dados de teste");
            if (r.BadZero > 0) t.Lines.Add("Blocos que voltaram vazios (nunca foram gravados de verdade): " + r.BadZero);
            if (r.BadAlias > 0) t.Lines.Add("Blocos sobrescritos por outros dados (o cartão regrava por cima do começo): " + r.BadAlias);
            if (r.BadOther > 0) t.Lines.Add("Blocos com dados corrompidos: " + r.BadOther);
            if (r.BadUnreadable > 0) t.Lines.Add("Blocos ilegíveis (erro de leitura): " + r.BadUnreadable);
            if (r.WriteError != null) t.Lines.Add("Erro de gravação: " + r.WriteError);
            if (r.EstimatedRealBytes >= 0 && r.Verdict == Verdict.Fake)
                t.Lines.Add("A capacidade real é uma estimativa: espaço já ocupado antes do teste + dados que sobreviveram.");
            if (r.WriteMBs > 0) t.Lines.Add("Velocidade média de gravação: " + Fmt.Speed(r.WriteMBs));
            if (r.ReadMBs > 0) t.Lines.Add("Velocidade média de leitura: " + Fmt.Speed(r.ReadMBs));
            t.Lines.Add("Duração: " + Fmt.Duration(r.Elapsed));
            return t;
        }
    }
}
