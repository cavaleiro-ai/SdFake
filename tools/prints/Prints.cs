// Gera os prints do programa de PC usados na documentacao (docs/telas/pc-*.png).
//
// Abre as janelas REAIS do SdFake e roda os motores REAIS de teste contra cartoes SIMULADOS
// (os mesmos de pc/tests/Cli.cs). Nao le nem grava em nenhum disco: so a lista de dispositivos
// e trocada por um cartao de exemplo. Os numeros do cartao falso sao os de um caso real:
// anuncia 133,3 GB e guarda cerca de 252 MB.
//
// Uso: powershell -ExecutionPolicy Bypass -File tools\prints.ps1
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SdFake
{
    /// <summary>Deixa o dispositivo simulado com a velocidade de um cartao de verdade (Teste Rapido).</summary>
    class SlowDevice : IBlockDevice
    {
        readonly IBlockDevice inner;
        int ops;
        public SlowDevice(IBlockDevice inner) { this.inner = inner; }
        public long Size { get { return inner.Size; } }
        public int SectorSize { get { return inner.SectorSize; } }
        void Pace() { if (++ops % 8 == 0) Thread.Sleep(1); }
        public void Read(long offset, IntPtr buffer, int bytes) { Pace(); inner.Read(offset, buffer, bytes); }
        public void Write(long offset, IntPtr buffer, int bytes) { Pace(); inner.Write(offset, buffer, bytes); }
    }

    /// <summary>O mesmo para o Teste Completo: espera um pouco a cada bloco gravado ou lido.</summary>
    class SlowStore : ITestStore
    {
        readonly ITestStore inner;
        readonly int writeMs, readMs;
        public SlowStore(ITestStore inner, int writeMs, int readMs) { this.inner = inner; this.writeMs = writeMs; this.readMs = readMs; }
        public void Prepare() { inner.Prepare(); }
        public long FreeBytes() { return inner.FreeBytes(); }
        public long TotalBytes() { return inner.TotalBytes(); }
        public void Cleanup() { inner.Cleanup(); }
        public ITestFile Create(string name) { return new SlowFile(this, inner.Create(name)); }
        public ITestFile Open(string name) { return new SlowFile(this, inner.Open(name)); }

        class SlowFile : ITestFile
        {
            readonly SlowStore s; readonly ITestFile f;
            public SlowFile(SlowStore s, ITestFile f) { this.s = s; this.f = f; }
            public void Write(IntPtr buffer, int bytes) { Thread.Sleep(s.writeMs); f.Write(buffer, bytes); }
            public void Read(long offset, IntPtr buffer, int bytes) { Thread.Sleep(s.readMs); f.Read(offset, buffer, bytes); }
            public void Flush() { f.Flush(); }
            public void Dispose() { f.Dispose(); }
        }
    }

    static class Prints
    {
        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int L, T, R, B; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);

        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        const long Declared128 = 133299998720L;   // o que o cartao falso anuncia ("128 GB" na embalagem)
        const long Real128 = 240L * 1024 * 1024;  // o que ele guarda de verdade (~252 MB)
        static string outDir;

        static void Pump(int ms)
        {
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(15); }
        }

        static void Shot(Form f, string name)
        {
            Pump(400);
            RECT r;
            GetWindowRect(f.Handle, out r);
            using (Bitmap bmp = new Bitmap(r.R - r.L, r.B - r.T))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    IntPtr hdc = g.GetHdc();
                    PrintWindow(f.Handle, hdc, 2);
                    g.ReleaseHdc(hdc);
                }
                bmp.Save(Path.Combine(outDir, name), ImageFormat.Png);
            }
            Console.WriteLine("  " + name);
        }

        /// <summary>
        /// Clica no botao "Seguir" do cabecalho e fotografa a janela com o menu aberto. O menu e uma janela
        /// a parte, entao aqui a captura e da tela (so o retangulo da janela, mantida na frente das outras).
        /// </summary>
        static void FollowMenu(DriveEntry d, string name)
        {
            MainForm f = Open(d);
            f.TopMost = true;
            f.Activate();
            Pump(400);
            Button follow = null;
            foreach (Control c in f.Controls)
                if (c is Header && c.Controls.Count > 0) follow = c.Controls[0] as Button;
            if (follow == null) throw new Exception("botão Seguir não encontrado no cabeçalho");
            follow.PerformClick();
            Pump(600);
            if (!AnyMenuOpen()) throw new Exception("o menu do botão Seguir não abriu");
            RECT r;
            GetWindowRect(f.Handle, out r);
            using (Bitmap bmp = new Bitmap(r.R - r.L, r.B - r.T))
            {
                using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.L, r.T, 0, 0, bmp.Size);
                bmp.Save(Path.Combine(outDir, name), ImageFormat.Png);
            }
            Console.WriteLine("  " + name);
            f.Close();
        }

        static bool AnyMenuOpen()
        {
            // ContextMenuStrip nao entra em Application.OpenForms: procura a janela de menu visivel desta thread
            bool found = false;
            EnumThreadWindows(GetCurrentThreadId(), delegate(IntPtr h, IntPtr l)
            {
                Control c = Control.FromHandle(h);
                if (c is ToolStripDropDown && c.Visible) { found = true; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumThreadWindows(uint thread, EnumProc proc, IntPtr l);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        static object Call(MainForm f, string method, params object[] args)
        {
            return typeof(MainForm).GetMethod(method, Any).Invoke(f, args);
        }

        static FieldInfo Field(string name) { return typeof(MainForm).GetField(name, Any); }

        /// <summary>Troca a lista de dispositivos pelo cartao de exemplo (o resto da tela e o proprio programa que monta).</summary>
        static void Stage(MainForm f, DriveEntry d, bool enabled)
        {
            ComboBox cb = (ComboBox)Field("cbDrive").GetValue(f);
            cb.Items.Clear();
            cb.Items.Add(d);
            cb.SelectedIndex = 0;
            cb.Enabled = enabled;
        }

        static DriveEntry Card(string label, string fs, string model, long total, long free, long device)
        {
            DriveEntry d = new DriveEntry();
            d.Letter = "E:"; d.Root = "E:\\"; d.Label = label; d.FileSystem = fs; d.Model = model;
            d.Total = total; d.Free = free; d.DeviceBytes = device; d.MediaBytes = device;
            d.Ready = true; d.Removable = true; d.Bus = 7;
            return d;
        }

        static MainForm Open(DriveEntry d)
        {
            MainForm f = new MainForm(null, null, 0);
            f.Show();
            Pump(1200);
            Stage(f, d, true);
            return f;
        }

        static void Quick(DriveEntry d, SimDevice dev, string midShot, string endShot)
        {
            MainForm f = Open(d);
            Call(f, "BeginRun", "Teste Rápido em " + d.Letter);
            FieldInfo latest = Field("latest");
            QuickTest test = new QuickTest();
            test.OnProgress = delegate(Progress p) { latest.SetValue(f, p); };
            QuickResult result = null;
            Exception error = null;
            Thread t = new Thread(delegate() { try { result = test.Run(new SlowDevice(dev)); } catch (Exception e) { error = e; } });
            t.Start();
            bool taken = midShot == null;
            while (t.IsAlive)
            {
                Application.DoEvents();
                Thread.Sleep(15);
                Progress p = (Progress)latest.GetValue(f);
                if (!taken && p != null && p.Fraction >= 0.42) { Shot(f, midShot); taken = true; }
            }
            if (error != null) throw error;
            Call(f, "Finish", QuickTest.Describe(result));
            Stage(f, d, true);
            Shot(f, endShot);
            f.Close();
        }

        static void Full(DriveEntry d, SimStore store, string midShot, string endShot)
        {
            MainForm f = Open(d);
            Call(f, "BeginRun", "Teste Completo em " + d.Letter);
            FieldInfo latest = Field("latest");
            FullTest test = new FullTest();
            test.DeviceBytes = d.DeviceBytes;
            test.OnProgress = delegate(Progress p) { latest.SetValue(f, p); };
            test.OnLog = delegate(string s) { f.BeginInvoke((MethodInvoker)delegate { Call(f, "Log", s); }); };
            FullResult result = null;
            Exception error = null;
            Thread t = new Thread(delegate() { try { result = test.Run(new SlowStore(store, 150, 50)); } catch (Exception e) { error = e; } });
            t.Start();
            bool taken = midShot == null;
            while (t.IsAlive)
            {
                Application.DoEvents();
                Thread.Sleep(15);
                Progress p = (Progress)latest.GetValue(f);
                if (!taken && p != null && p.Fraction >= 0.3) { Shot(f, midShot); taken = true; }
            }
            if (error != null) throw error;
            Pump(200);
            Call(f, "Finish", FullTest.Describe(result));
            Stage(f, d, true);
            Shot(f, endShot);
            f.Close();
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length < 1) { Console.WriteLine("uso: SdFakePrints <pasta de saida>"); return 2; }
            outDir = args[0];
            Directory.CreateDirectory(outDir);
            Native.SetErrorMode(1);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                using (WelcomeForm w = new WelcomeForm())
                {
                    w.Show();
                    Shot(w, "pc-boas-vindas.png");
                    w.Close();
                }

                // cartao verdadeiro de 64 GB
                DriveEntry real64 = Card("CARTAO SD", "exFAT", "Generic STORAGE DEVICE", 63848841216L, 63839404032L, 63864569856L);
                MainForm start = Open(real64);
                Shot(start, "pc-inicio.png");
                start.Close();
                FollowMenu(real64, "pc-seguir.png");
                Quick(real64, new SimDevice { Declared = 63864569856L, Real = 63864569856L, Mode = SimMode.Genuine }, null, "pc-verdadeiro.png");

                // cartao falso como chega do vendedor: formatado com os "128 GB" que anuncia
                DriveEntry fake = Card("SD 128GB", "exFAT", "Mass Storage Device", 133283373056L, 133270000000L, Declared128);
                Quick(fake, new SimDevice { Declared = Declared128, Real = Real128, Mode = SimMode.Drop }, "pc-testando.png", "pc-falso.png");

                // o mesmo cartao falso depois de reformatado no tamanho real: o Teste Completo so alcanca os 243 MB
                DriveEntry shrunk = Card("SD 128GB", "FAT32", "Mass Storage Device", 243000000L, 238000000L, Declared128);
                Full(shrunk, new SimStore { Declared = 243000000L, Real = 243000000L, UsedBefore = 5L * 1024 * 1024, Mode = SimMode.Genuine },
                    "pc-teste-completo.png", "pc-nao-confirmado.png");
                return 0;
            }
            catch (Exception e)
            {
                Console.WriteLine("ERRO: " + e);
                return 1;
            }
        }
    }
}
