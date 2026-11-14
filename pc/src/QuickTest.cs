// SdFake - Teste Rapido: amostra pontos espalhados por todo o dispositivo.
// Em cada ponto: guarda o conteudo original, grava um padrao unico, le de volta e
// restaura o original. Os pontos ficam numa grade com passo potencia de 2, de modo
// que cartoes que "dao a volta" (ignoram os bits altos do endereco) fazem um ponto
// sobrescrever outro e sao denunciados.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SdFake
{
    public class QuickResult
    {
        public Verdict Verdict;
        public long DeclaredBytes;
        public long RealBytes = -1;
        public bool RealIsUpperBound;
        public int Samples, Bad, Dropped, Aliased, Garbage, IoErrors;
        public bool WrapAround;
        public bool Cancelled;
        public bool RestoreProblem;
        public string FatalError;
        public TimeSpan Elapsed;
    }

    public class QuickTest
    {
        public volatile bool CancelRequested;
        public Action<Progress> OnProgress;
        public int MaxSamples = 512;
        public int ReportIntervalMs = 150;

        const int StOk = 0, StReadErr = 1, StWriteErr = 2, StDropped = 3, StAliased = 4, StGarbage = 5;

        IBlockDevice dev;
        int chunk;
        long size;
        long[] pos;
        byte[][] orig;
        int[] st;
        int[] aliasOf;
        bool[] touched;
        ulong seed;
        AlignedBuffer a, b, c;
        readonly Stopwatch throttle = Stopwatch.StartNew();
        long lastReportMs = -1000;

        void Report(string phase, double fraction, string detail, bool force)
        {
            if (OnProgress == null) return;
            long now = throttle.ElapsedMilliseconds;
            if (!force && now - lastReportMs < ReportIntervalMs) return;
            lastReportMs = now;
            Progress p = new Progress();
            p.Phase = phase;
            p.Fraction = fraction;
            p.Detail = detail;
            OnProgress(p);
        }

        public QuickResult Run(IBlockDevice device)
        {
            QuickResult r = new QuickResult();
            Stopwatch clock = Stopwatch.StartNew();
            dev = device;
            try
            {
                chunk = 4096;
                if (dev.SectorSize > 0 && chunk % dev.SectorSize != 0) chunk = dev.SectorSize * 8;
                r.DeclaredBytes = dev.Size;
                size = dev.Size - dev.Size % chunk;
                if (size < 4L * 1024 * 1024)
                    throw new TestAbortException("O dispositivo é pequeno demais para o teste rápido.");

                long stride = 65536;
                while (stride < 2L * chunk || size / stride > MaxSamples) stride <<= 1;
                List<long> list = new List<long>();
                for (long p = stride / 2; p + chunk <= size; p += stride) list.Add(p);
                long tail = size - chunk;
                if (list.Count == 0 || tail > list[list.Count - 1]) list.Add(tail);
                pos = list.ToArray();
                int n = pos.Length;
                r.Samples = n;

                a = new AlignedBuffer(chunk);
                b = new AlignedBuffer(chunk);
                c = new AlignedBuffer(chunk);
                seed = Pattern.NewSeed();
                orig = new byte[n][];
                st = new int[n];
                aliasOf = new int[n];
                touched = new bool[n];

                try
                {
                    bool finished = Phases(r, n);
                    if (!finished) r.Cancelled = true;
                    if (finished) Analyze(r, n);
                }
                finally
                {
                    Restore(r, n);
                }

                if (r.Cancelled) r.Verdict = Verdict.Incomplete;
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
                if (a != null) a.Dispose();
                if (b != null) b.Dispose();
                if (c != null) c.Dispose();
                r.Elapsed = clock.Elapsed;
            }
            return r;
        }

        bool Phases(QuickResult r, int n)
        {
            // 1) guarda o conteudo original de cada ponto
            for (int i = 0; i < n; i++)
            {
                if (CancelRequested) return false;
                try
                {
                    dev.Read(pos[i], a.Ptr, chunk);
                    orig[i] = new byte[chunk];
                    Marshal.Copy(a.Ptr, orig[i], 0, chunk);
                }
                catch (TestIOException e)
                {
                    if (e.DeviceGone) throw;
                    st[i] = StReadErr;
                }
                Report("Lendo o conteúdo atual (1 de 4)", 0.25 * (i + 1) / n, "ponto " + (i + 1) + " de " + n, false);
            }

            // 2) grava um padrao unico em cada ponto
            for (int i = 0; i < n; i++)
            {
                if (CancelRequested) return false;
                if (st[i] != StOk) continue;
                Pattern.Fill(a.Ptr, chunk, seed, (ulong)i);
                touched[i] = true;
                try { dev.Write(pos[i], a.Ptr, chunk); }
                catch (TestIOException e)
                {
                    if (e.DeviceGone) throw;
                    if (e.WriteProtected)
                        throw new TestAbortException("O dispositivo está protegido contra gravação. Destrave a chavinha lateral do cartão/adaptador (LOCK) e tente de novo.");
                    st[i] = StWriteErr;
                }
                Report("Gravando pontos de teste (2 de 4)", 0.25 + 0.25 * (i + 1) / n, "ponto " + (i + 1) + " de " + n, false);
            }

            // 3) le tudo de volta (so depois de gravar todos, para pegar sobrescritas)
            for (int i = 0; i < n; i++)
            {
                if (CancelRequested) return false;
                aliasOf[i] = -1;
                if (st[i] != StOk) continue;
                try { dev.Read(pos[i], b.Ptr, chunk); }
                catch (TestIOException e)
                {
                    if (e.DeviceGone) throw;
                    st[i] = StReadErr;
                    continue;
                }
                Pattern.Fill(a.Ptr, chunk, seed, (ulong)i);
                if (!Pattern.Equal(a.Ptr, b.Ptr, chunk))
                {
                    ulong key = Pattern.StampKey(b.Ptr);
                    if (Pattern.StampSeed(b.Ptr) == seed && key < (ulong)n && key != (ulong)i)
                    {
                        st[i] = StAliased;
                        aliasOf[i] = (int)key;
                    }
                    else if (SameAsOriginal(i)) st[i] = StDropped;
                    else st[i] = StGarbage;
                }
                Report("Conferindo (3 de 4)", 0.5 + 0.25 * (i + 1) / n, "ponto " + (i + 1) + " de " + n, false);
            }
            return true;
        }

        bool SameAsOriginal(int i)
        {
            if (orig[i] == null) return false;
            Marshal.Copy(orig[i], 0, c.Ptr, chunk);
            return Pattern.Equal(b.Ptr, c.Ptr, chunk);
        }

        void Analyze(QuickResult r, int n)
        {
            int firstBad = -1;
            for (int i = 0; i < n; i++)
            {
                if (st[i] == StOk) continue;
                r.Bad++;
                if (firstBad < 0) firstBad = i;
                if (st[i] == StDropped) r.Dropped++;
                else if (st[i] == StAliased) r.Aliased++;
                else if (st[i] == StGarbage) r.Garbage++;
                else r.IoErrors++;
            }
            if (r.Bad == 0) { r.Verdict = Verdict.Genuine; return; }

            Report("Medindo a capacidade real", 0.78, "", true);

            if (r.Aliased > 0)
            {
                r.WrapAround = true;
                r.Verdict = Verdict.Fake;
                long modulus = FindWrapModulus();
                if (modulus > 0) r.RealBytes = modulus;
                else
                {
                    long min = long.MaxValue;
                    for (int i = 0; i < n; i++)
                    {
                        if (st[i] != StAliased) continue;
                        long d = Math.Abs(pos[aliasOf[i]] - pos[i]);
                        if (d > 0 && d < min) min = d;
                    }
                    if (min != long.MaxValue) { r.RealBytes = min; r.RealIsUpperBound = true; }
                }
                return;
            }

            int tailBad = 0;
            for (int i = firstBad; i < n; i++) if (st[i] != StOk) tailBad++;
            if (tailBad * 2 >= n - firstBad)
            {
                long lo = firstBad == 0 ? 0 : pos[firstBad - 1];
                long hi = pos[firstBad];
                long precision = Math.Max((long)chunk, 65536);
                int guard = 0;
                while (hi - lo > precision && guard++ < 64 && !CancelRequested)
                {
                    long mid = lo + (hi - lo) / 2;
                    mid -= mid % chunk;
                    if (mid <= lo) break;
                    if (Probe(mid)) lo = mid; else hi = mid;
                }
                r.RealBytes = hi;
                r.Verdict = hi < size * 0.95 ? Verdict.Fake : Verdict.Defective;
            }
            else
            {
                r.Verdict = Verdict.Defective;
            }
        }

        /// <summary>Grava/le/restaura um bloco avulso. true = o dispositivo guardou o dado.</summary>
        bool Probe(long offset)
        {
            bool haveOriginal = false;
            bool ok = false;
            try
            {
                dev.Read(offset, c.Ptr, chunk);
                haveOriginal = true;
                Pattern.Fill(a.Ptr, chunk, seed, 0x8000000000000000UL | (ulong)(offset / chunk));
                dev.Write(offset, a.Ptr, chunk);

                // regrava alguns pontos bons (com o mesmo conteudo) para esvaziar eventual cache do controlador
                int flushed = 0;
                for (int i = 0; i < st.Length && flushed < 4; i++)
                {
                    if (st[i] != StOk) continue;
                    Pattern.Fill(b.Ptr, chunk, seed, (ulong)i);
                    dev.Write(pos[i], b.Ptr, chunk);
                    flushed++;
                }

                dev.Read(offset, b.Ptr, chunk);
                Pattern.Fill(a.Ptr, chunk, seed, 0x8000000000000000UL | (ulong)(offset / chunk));
                ok = Pattern.Equal(a.Ptr, b.Ptr, chunk);
            }
            catch (TestIOException e)
            {
                if (e.DeviceGone) throw;
                ok = false;
            }
            if (haveOriginal)
            {
                try { dev.Write(offset, c.Ptr, chunk); }
                catch (TestIOException e) { if (e.DeviceGone) throw; }
            }
            return ok;
        }

        /// <summary>
        /// Descobre a partir de que distancia (potencia de 2) uma gravacao "cai em cima" do
        /// primeiro ponto. Essa distancia e o tamanho real do armazenamento.
        /// </summary>
        long FindWrapModulus()
        {
            if (orig[0] == null) return -1;
            long b0 = pos[0];
            ulong k0 = 0xC000000000000000UL;
            try
            {
                Pattern.Fill(a.Ptr, chunk, seed, k0);
                touched[0] = true;
                dev.Write(b0, a.Ptr, chunk);
                for (long d = chunk; b0 + d + chunk <= size; d <<= 1)
                {
                    if (CancelRequested) return -1;
                    long t = b0 + d;
                    dev.Read(t, c.Ptr, chunk);
                    Pattern.Fill(a.Ptr, chunk, seed, 0xA000000000000000UL | (ulong)(d / chunk));
                    dev.Write(t, a.Ptr, chunk);
                    dev.Read(b0, b.Ptr, chunk);
                    Pattern.Fill(a.Ptr, chunk, seed, k0);
                    bool intact = Pattern.Equal(a.Ptr, b.Ptr, chunk);
                    dev.Write(t, c.Ptr, chunk);
                    if (!intact) return d;
                }
            }
            catch (TestIOException e)
            {
                if (e.DeviceGone) throw;
            }
            return -1;
        }

        /// <summary>Devolve o conteudo original. Pontos ruins primeiro, bons por ultimo (os bons e que valem).</summary>
        void Restore(QuickResult r, int n)
        {
            List<int> order = new List<int>();
            for (int i = 0; i < n; i++) if (st[i] != StOk) order.Add(i);
            for (int i = n - 1; i >= 0; i--) if (st[i] == StOk) order.Add(i);

            bool gone = false;
            int done = 0;
            foreach (int i in order)
            {
                done++;
                if (!touched[i] || orig[i] == null || gone) { if (gone && touched[i] && st[i] == StOk) r.RestoreProblem = true; continue; }
                Marshal.Copy(orig[i], 0, a.Ptr, chunk);
                try { dev.Write(pos[i], a.Ptr, chunk); }
                catch (TestIOException e)
                {
                    if (e.DeviceGone) gone = true;
                    if (st[i] == StOk) r.RestoreProblem = true;
                }
                Report("Restaurando o conteúdo original (4 de 4)", 0.85 + 0.1 * done / order.Count, "", false);
            }

            // confere a restauracao nos pontos que realmente guardam dados
            if (!gone && !r.WrapAround)
            {
                for (int i = 0; i < n; i++)
                {
                    if (st[i] != StOk || !touched[i] || orig[i] == null) continue;
                    try
                    {
                        dev.Read(pos[i], b.Ptr, chunk);
                        if (!SameAsOriginal(i)) r.RestoreProblem = true;
                    }
                    catch (TestIOException)
                    {
                        r.RestoreProblem = true;
                    }
                }
            }
            Report("Concluído", 1.0, "", true);
        }

        // ------------------------------------------------------------------ relatorio

        public static ReportText Describe(QuickResult r)
        {
            ReportText t = new ReportText();
            t.Verdict = r.Verdict;
            switch (r.Verdict)
            {
                case Verdict.Genuine:
                    t.Title = "VERDADEIRO";
                    t.Summary = "Os " + Fmt.Bytes(r.DeclaredBytes) + " anunciados existem de verdade: "
                              + r.Samples + " pontos espalhados por todo o dispositivo guardaram os dados corretamente.";
                    break;
                case Verdict.Fake:
                    t.Title = "FALSO";
                    if (r.RealBytes >= 0)
                        t.Summary = "Capacidade real " + (r.RealIsUpperBound ? "de no máximo " : "aproximada: ") + Fmt.Bytes(r.RealBytes)
                                  + "  —  anunciado: " + Fmt.Bytes(r.DeclaredBytes) + ".";
                    else
                        t.Summary = "A capacidade real é bem menor que os " + Fmt.Bytes(r.DeclaredBytes) + " anunciados.";
                    break;
                case Verdict.Defective:
                    t.Title = "COM DEFEITO";
                    t.Summary = r.Bad + " de " + r.Samples + " pontos testados não guardaram os dados corretamente. "
                              + "A capacidade parece verdadeira, mas o dispositivo não é confiável.";
                    break;
                case Verdict.Incomplete:
                    t.Title = "TESTE INCOMPLETO";
                    t.Summary = "Teste cancelado antes do fim; não dá para concluir nada.";
                    break;
                default:
                    t.Title = "ERRO";
                    t.Summary = r.FatalError != null ? r.FatalError : "Erro inesperado.";
                    break;
            }

            if (r.DeclaredBytes > 0) t.Lines.Add("Capacidade anunciada pelo dispositivo: " + Fmt.Bytes(r.DeclaredBytes));
            if (r.Samples > 0) t.Lines.Add("Pontos testados: " + r.Samples + "  ·  com falha: " + r.Bad);
            if (r.Dropped > 0) t.Lines.Add("Gravações ignoradas (o dispositivo finge que gravou): " + r.Dropped);
            if (r.Aliased > 0) t.Lines.Add("Pontos sobrescritos por outros (o endereço \"dá a volta\"): " + r.Aliased);
            if (r.Garbage > 0) t.Lines.Add("Pontos que devolveram dados corrompidos: " + r.Garbage);
            if (r.IoErrors > 0) t.Lines.Add("Pontos com erro de leitura/gravação: " + r.IoErrors);
            if (r.Verdict == Verdict.Genuine)
                t.Lines.Add("Este teste é por amostragem. Para conferir cada setor (e achar defeitos pontuais), rode o Teste Completo.");
            if (r.RestoreProblem)
                t.Lines.Add("ATENÇÃO: não foi possível restaurar todo o conteúdo original. Arquivos que estavam no dispositivo podem ter sido afetados.");
            else if (r.Samples > 0 && r.Verdict != Verdict.Error)
                t.Lines.Add("O conteúdo original dos pontos testados foi restaurado.");
            t.Lines.Add("Duração: " + Fmt.Duration(r.Elapsed));
            return t;
        }
    }
}
