// SdFakeCli - ferramenta de linha de comando e autoteste dos motores.
//   SdFakeCli selftest            roda os motores contra cartoes simulados (verdadeiro, falso, defeituoso)
//   SdFakeCli list                lista as unidades removiveis
//   SdFakeCli full <pasta|E:\> [limiteMiB]   Teste Completo
//   SdFakeCli quick <E:>          Teste Rapido (precisa de administrador)
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace SdFake
{
    enum SimMode { Genuine, Drop, Wrap, BadSpot, RefuseWrites }

    /// <summary>Cartao simulado para o Teste Completo. Guarda so o carimbo de cada bloco de 1 MiB.</summary>
    class SimStore : ITestStore
    {
        public long Declared, Real, UsedBefore;
        public SimMode Mode;
        public long BadSpotStart = -1, BadSpotBlocks;
        const int BS = Pattern.BlockSize;

        readonly Dictionary<long, KeyValuePair<ulong, ulong>> blocks = new Dictionary<long, KeyValuePair<ulong, ulong>>();
        readonly Dictionary<string, long> start = new Dictionary<string, long>();
        long next;
        public bool CleanedUp;

        public void Prepare() { next = UsedBefore / BS; }
        public long FreeBytes() { return Declared - next * BS; }
        public long TotalBytes() { return Declared; }
        public void Cleanup() { CleanedUp = true; }

        public ITestFile Create(string name)
        {
            start[name] = next;
            return new SimFile(this, next, true);
        }

        public ITestFile Open(string name) { return new SimFile(this, start[name], false); }

        class SimFile : ITestFile
        {
            readonly SimStore s; readonly long first; long count; readonly bool writing;
            public SimFile(SimStore s, long first, bool writing) { this.s = s; this.first = first; this.writing = writing; }

            public void Write(IntPtr buffer, int bytes)
            {
                long addr = first + count;
                if ((addr + 1) * BS > s.Declared) throw new TestIOException("disco cheio (simulado)", 112);
                if (s.Mode == SimMode.RefuseWrites && addr * BS >= s.Real) throw new TestIOException("erro de E/S (simulado)", 1117);
                count++;
                s.next = addr + 1;
                KeyValuePair<ulong, ulong> stamp = new KeyValuePair<ulong, ulong>(Pattern.StampSeed(buffer), Pattern.StampKey(buffer));
                if (s.Mode == SimMode.Drop && addr * BS >= s.Real) return;
                if (s.Mode == SimMode.Wrap) addr %= (s.Real / BS);
                s.blocks[addr] = stamp;
            }

            public void Read(long offset, IntPtr buffer, int bytes)
            {
                long addr = first + offset / BS;
                long phys = s.Mode == SimMode.Wrap ? addr % (s.Real / BS) : addr;
                KeyValuePair<ulong, ulong> stamp;
                if (!s.blocks.TryGetValue(phys, out stamp))
                {
                    for (int i = 0; i < bytes; i += 8) Marshal.WriteInt64(buffer, i, 0);
                    return;
                }
                Pattern.Fill(buffer, bytes, stamp.Key, stamp.Value);
                if (s.Mode == SimMode.BadSpot && addr >= s.BadSpotStart && addr < s.BadSpotStart + s.BadSpotBlocks)
                    Marshal.WriteInt64(buffer, 8192, 0x0BADBADBADBADBADL);
            }

            public void Flush() { }
            public void Dispose() { }
        }
    }

    /// <summary>Dispositivo de blocos simulado para o Teste Rapido (esparso, por pedacos de 4 KiB).</summary>
    class SimDevice : IBlockDevice
    {
        public long Declared, Real;
        public SimMode Mode;
        public HashSet<long> BadOffsets = new HashSet<long>();
        public int Writes, Reads;
        readonly Dictionary<long, byte[]> chunks = new Dictionary<long, byte[]>();
        const int C = 4096;

        public long Size { get { return Declared; } }
        public int SectorSize { get { return 512; } }

        static void Original(long phys, IntPtr buffer)
        {
            Pattern.Fill(buffer, C, 0x0123456789ABCDEFUL, (ulong)(phys / C) | 0x7000000000000000UL);
        }

        long Map(long offset)
        {
            return Mode == SimMode.Wrap ? offset % Real : offset;
        }

        public void Read(long offset, IntPtr buffer, int bytes)
        {
            Reads++;
            if (bytes != C || offset % C != 0 || offset + bytes > Declared) throw new TestIOException("leitura inválida", 87);
            if (Mode == SimMode.Drop && offset >= Real)
            {
                for (int i = 0; i < C; i += 8) Marshal.WriteInt64(buffer, i, -1L);
                return;
            }
            long phys = Map(offset);
            byte[] data;
            if (chunks.TryGetValue(phys, out data)) Marshal.Copy(data, 0, buffer, C);
            else Original(phys, buffer);
            if (BadOffsets.Contains(offset)) Marshal.WriteInt64(buffer, 64, 0x0BADBADBADBADBADL);
        }

        public void Write(long offset, IntPtr buffer, int bytes)
        {
            Writes++;
            if (bytes != C || offset % C != 0 || offset + bytes > Declared) throw new TestIOException("gravação inválida", 87);
            if (Mode == SimMode.Drop && offset >= Real) return;
            byte[] data = new byte[C];
            Marshal.Copy(buffer, data, 0, C);
            chunks[Map(offset)] = data;
        }

        /// <summary>Quantos pedacos ficaram diferentes do conteudo original depois do teste.</summary>
        public int ModifiedChunks()
        {
            int modified = 0;
            using (AlignedBuffer a = new AlignedBuffer(C))
            {
                byte[] expected = new byte[C];
                foreach (KeyValuePair<long, byte[]> kv in chunks)
                {
                    Original(kv.Key, a.Ptr);
                    Marshal.Copy(a.Ptr, expected, 0, C);
                    for (int i = 0; i < C; i++)
                    {
                        if (expected[i] != kv.Value[i]) { modified++; break; }
                    }
                }
            }
            return modified;
        }
    }

    static class Cli
    {
        static int failures;

        static void Check(string name, bool ok, string detail)
        {
            Console.WriteLine((ok ? "  [ok]    " : "  [FALHA] ") + name + (detail.Length > 0 ? "  (" + detail + ")" : ""));
            if (!ok) failures++;
        }

        static bool Near(long value, long expected, long tolerance)
        {
            return Math.Abs(value - expected) <= tolerance;
        }

        static FullResult RunFullSim(SimStore s)
        {
            FullTest t = new FullTest();
            FullResult r = t.Run(s);
            return r;
        }

        static string Sum(FullResult r)
        {
            return r.Verdict + ", gravado " + Fmt.Bytes(r.WrittenBytes) + ", ok " + Fmt.Bytes(r.OkBytes) + ", ruim " + Fmt.Bytes(r.BadBytes)
                + (r.EstimatedRealBytes >= 0 ? ", real~" + Fmt.Bytes(r.EstimatedRealBytes) : "") + ", " + Fmt.Duration(r.Elapsed);
        }

        static string Sum(QuickResult r)
        {
            return r.Verdict + ", pontos " + r.Samples + ", ruins " + r.Bad
                + (r.RealBytes >= 0 ? ", real~" + Fmt.Bytes(r.RealBytes) : "") + (r.WrapAround ? ", wrap" : "") + ", " + Fmt.Duration(r.Elapsed);
        }

        const long MiB = 1L << 20, GiB = 1L << 30;

        static int SelfTest(string scratch)
        {
            Console.WriteLine("== Teste Completo contra cartões simulados ==");

            SimStore s = new SimStore { Declared = 3000 * MiB, Real = 3000 * MiB, Mode = SimMode.Genuine };
            FullResult r = RunFullSim(s);
            Check("verdadeiro de 3 GB", r.Verdict == Verdict.Genuine && r.WrittenBytes == r.TargetBytes && r.BadBytes == 0 && s.CleanedUp, Sum(r));

            s = new SimStore { Declared = 128000000000L, Real = 200 * MiB, Mode = SimMode.Drop };
            r = RunFullSim(s);
            Check("falso: anuncia 128 GB, tem 200 MiB (descarta gravações)",
                r.Verdict == Verdict.Fake && Near(r.EstimatedRealBytes, 200 * MiB, MiB) && r.WrittenBytes < 2 * GiB && r.BadZero > 0, Sum(r));

            s = new SimStore { Declared = 64 * GiB, Real = 256 * MiB, Mode = SimMode.Wrap };
            r = RunFullSim(s);
            Check("falso: anuncia 64 GB, tem 256 MiB (regrava por cima)",
                r.Verdict == Verdict.Fake && Near(r.EstimatedRealBytes, 256 * MiB, 2 * MiB) && r.WrittenBytes < 4 * GiB && r.BadAlias > 0, Sum(r));

            s = new SimStore { Declared = 32 * GiB, Real = 500 * MiB, UsedBefore = 100 * MiB, Mode = SimMode.Drop };
            r = RunFullSim(s);
            Check("falso com 100 MiB já ocupados: real 500 MiB",
                r.Verdict == Verdict.Fake && Near(r.EstimatedRealBytes, 500 * MiB, MiB), Sum(r));

            s = new SimStore { Declared = 16 * GiB, Real = 1000 * MiB, Mode = SimMode.RefuseWrites };
            r = RunFullSim(s);
            Check("falso que dá erro de gravação após 1000 MiB",
                r.Verdict == Verdict.Fake && r.WriteError != null && Near(r.EstimatedRealBytes, 1000 * MiB, MiB), Sum(r));

            s = new SimStore { Declared = 2 * GiB, Real = 2 * GiB, Mode = SimMode.BadSpot, BadSpotStart = 700, BadSpotBlocks = 3 };
            r = RunFullSim(s);
            Check("verdadeiro com 3 blocos defeituosos",
                r.Verdict == Verdict.Defective && r.WrittenBytes == r.TargetBytes && r.BadBytes == 3 * 4096 && r.BadOther == 3, Sum(r));

            // cartao falso ja reformatado no tamanho real: o volume (243 MB) funciona, mas o dispositivo anuncia 133,3 GB
            s = new SimStore { Declared = 243000000L, Real = 243000000L, Mode = SimMode.Genuine };
            FullTest pt = new FullTest();
            pt.DeviceBytes = 133300000000L;
            r = pt.Run(s);
            ReportText prt = FullTest.Describe(r);
            Check("falso reformatado: volume de 243 MB num dispositivo que anuncia 133,3 GB não pode dar VERDADEIRO",
                r.Verdict == Verdict.Partial && r.BadBytes == 0 && prt.Title == "NÃO CONFIRMADO" && prt.Summary.Contains("133,3 GB"), Sum(r));

            s = new SimStore { Declared = 124 * GiB / 64, Real = 124 * GiB / 64, Mode = SimMode.Genuine };
            pt = new FullTest();
            pt.DeviceBytes = s.Declared + 32 * MiB;   // volume ocupa quase o dispositivo todo (sobra so a tabela de particoes)
            r = pt.Run(s);
            Check("verdadeiro: volume ocupa o dispositivo inteiro", r.Verdict == Verdict.Genuine, Sum(r));

            s = new SimStore { Declared = 2 * GiB, Real = 2 * GiB, Mode = SimMode.Genuine };
            FullTest ft = new FullTest();
            int calls = 0;
            ft.ReportIntervalMs = 0;
            ft.OnProgress = delegate(Progress p) { if (++calls == 300) ft.CancelRequested = true; };
            r = ft.Run(s);
            Check("cancelamento", r.Verdict == Verdict.Incomplete && r.Cancelled && s.CleanedUp, Sum(r));

            Console.WriteLine("== Teste Completo em arquivos reais (sem cache do Windows) ==");
            Directory.CreateDirectory(scratch);
            WinStore ws = new WinStore(scratch);
            ft = new FullTest();
            ft.LimitBytes = 56 * MiB;
            r = ft.Run(ws);
            bool gone = !Directory.Exists(Path.Combine(scratch, WinStore.FolderName));
            Check("56 MiB gravados e conferidos no disco, pasta de teste removida",
                r.Verdict == Verdict.Genuine && r.WrittenBytes == 56 * MiB && r.OkBytes == 56 * MiB && gone,
                Sum(r) + (r.FatalError != null ? " " + r.FatalError : ""));

            Console.WriteLine("== Teste Rápido contra dispositivos simulados ==");

            SimDevice d = new SimDevice { Declared = 128000000000L, Real = 128000000000L, Mode = SimMode.Genuine };
            QuickResult q = new QuickTest().Run(d);
            Check("verdadeiro de 128 GB", q.Verdict == Verdict.Genuine && q.Bad == 0 && d.ModifiedChunks() == 0 && !q.RestoreProblem,
                Sum(q) + ", gravações " + d.Writes);

            d = new SimDevice { Declared = 128000000000L, Real = 200 * MiB, Mode = SimMode.Drop };
            q = new QuickTest().Run(d);
            Check("falso: anuncia 128 GB, tem 200 MiB (descarta gravações)",
                q.Verdict == Verdict.Fake && Near(q.RealBytes, 200 * MiB, 65536) && d.ModifiedChunks() == 0 && !q.RestoreProblem, Sum(q));

            d = new SimDevice { Declared = 64 * GiB, Real = 7 * GiB + 300 * MiB, Mode = SimMode.Drop };
            q = new QuickTest().Run(d);
            Check("falso: anuncia 64 GiB, tem 7,3 GiB",
                q.Verdict == Verdict.Fake && Near(q.RealBytes, 7 * GiB + 300 * MiB, 65536) && d.ModifiedChunks() == 0, Sum(q));

            d = new SimDevice { Declared = 128 * GiB, Real = 8 * GiB, Mode = SimMode.Wrap };
            q = new QuickTest().Run(d);
            Check("falso: anuncia 128 GiB, endereço dá a volta em 8 GiB",
                q.Verdict == Verdict.Fake && q.WrapAround && q.RealBytes == 8 * GiB && d.ModifiedChunks() == 0, Sum(q));

            d = new SimDevice { Declared = 128 * GiB, Real = 32 * MiB, Mode = SimMode.Wrap };
            q = new QuickTest().Run(d);
            Check("falso: anuncia 128 GiB, endereço dá a volta em 32 MiB",
                q.Verdict == Verdict.Fake && q.WrapAround && q.RealBytes == 32 * MiB && d.ModifiedChunks() == 0, Sum(q));

            d = new SimDevice { Declared = 32 * GiB, Real = 32 * GiB, Mode = SimMode.Genuine };
            // tres pontos defeituosos na grade de amostragem (passo 64 MiB, primeiro ponto em 32 MiB)
            d.BadOffsets.Add(32 * MiB + 64 * MiB * 10);
            d.BadOffsets.Add(32 * MiB + 64 * MiB * 200);
            d.BadOffsets.Add(32 * MiB + 64 * MiB * 201);
            q = new QuickTest().Run(d);
            Check("verdadeiro com 3 pontos defeituosos", q.Verdict == Verdict.Defective && q.Bad == 3, Sum(q));

            d = new SimDevice { Declared = 16 * GiB, Real = 16 * GiB, Mode = SimMode.Genuine };
            QuickTest qt = new QuickTest();
            qt.ReportIntervalMs = 0;
            qt.OnProgress = delegate(Progress p) { if (p.Phase.StartsWith("Gravando") && p.Fraction > 0.35) qt.CancelRequested = true; };
            q = qt.Run(d);
            Check("cancelamento restaura o conteúdo", q.Verdict == Verdict.Incomplete && d.ModifiedChunks() == 0 && !q.RestoreProblem, Sum(q));

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "TODOS OS TESTES PASSARAM" : failures + " TESTE(S) FALHARAM");
            return failures == 0 ? 0 : 1;
        }

        static void Print(ReportText t)
        {
            Console.WriteLine();
            Console.WriteLine("### " + t.Title + " ###");
            Console.WriteLine(t.Summary);
            foreach (string line in t.Lines) Console.WriteLine(" - " + line);
        }

        static void ShowProgress(Progress p)
        {
            string line = p.Phase + "  " + Fmt.Percent(p.Fraction) + "  " + p.Detail;
            if (line.Length > 110) line = line.Substring(0, 110);
            Console.Write("\r" + line.PadRight(110));
        }

        static int Main(string[] args)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (Exception) { }
            Native.SetErrorMode(1);
            string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            if (cmd == "selftest")
            {
                string scratch = args.Length > 1 ? args[1] : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-tmp");
                int code = SelfTest(scratch);
                try { if (Directory.GetFileSystemEntries(scratch).Length == 0) Directory.Delete(scratch); } catch (Exception) { }
                return code;
            }
            if (cmd == "list")
            {
                List<DriveEntry> list = DriveCatalog.List();
                if (list.Count == 0) Console.WriteLine("Nenhum cartão ou pendrive encontrado.");
                foreach (DriveEntry e in list)
                    Console.WriteLine(e + (e.Ready ? "  [" + e.FileSystem + ", livre " + Fmt.Bytes(e.Free) + "]" : "")
                        + "  dispositivo=" + Fmt.Bytes(e.DeviceBytes) + " barramento=" + e.Bus + " modelo=" + e.Model);
                return 0;
            }
            if (cmd == "full" && args.Length >= 2)
            {
                FullTest t = new FullTest();
                if (args.Length >= 3) t.LimitBytes = long.Parse(args[2]) * MiB;
                if (args[1].Length <= 3) t.DeviceBytes = Math.Max(0, DriveCatalog.DeviceLengthOf(args[1]));   // "E:" ou "E:\"
                t.OnProgress = ShowProgress;
                t.OnLog = delegate(string s) { Console.WriteLine(); Console.WriteLine(s); };
                Console.CancelKeyPress += delegate(object o, ConsoleCancelEventArgs e) { e.Cancel = true; t.CancelRequested = true; };
                Native.KeepAwake(true);
                FullResult r = t.Run(new WinStore(args[1]));
                Print(FullTest.Describe(r));
                return r.Verdict == Verdict.Genuine ? 0 : 2;
            }
            if (cmd == "quick" && args.Length >= 2)
            {
                QuickTest t = new QuickTest();
                t.OnProgress = ShowProgress;
                Console.CancelKeyPress += delegate(object o, ConsoleCancelEventArgs e) { e.Cancel = true; t.CancelRequested = true; };
                QuickResult r;
                try
                {
                    using (RawDisk disk = RawDisk.Open(args[1]))
                    {
                        r = t.Run(disk);
                    }
                }
                catch (TestAbortException e)
                {
                    Console.WriteLine("ERRO: " + e.Message + (e.NeedAdmin ? " Abra o terminal como administrador." : ""));
                    return 3;
                }
                Print(QuickTest.Describe(r));
                return r.Verdict == Verdict.Genuine ? 0 : 2;
            }
            Console.WriteLine("Uso: SdFakeCli selftest | list | full <pasta ou E:\\> [limiteMiB] | quick <E:>");
            return 1;
        }
    }
}
