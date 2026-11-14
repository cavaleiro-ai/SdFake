// Instalador/desinstalador do SdFake. Instala por usuario (sem administrador) em
// %LOCALAPPDATA%\Programs\SdFake, cria atalhos e registra em "Aplicativos instalados".
//
//   SdFake-Instalador.exe                 assistente grafico
//   SdFake-Instalador.exe /S              instalacao silenciosa
//   SdFake-Instalador.exe /uninstall      desinstalar (e o que o Windows chama)
//   SdFake-Instalador.exe /TESTDIR=<dir> [/UNINSTALL]   teste: tudo dentro de <dir>, sem registro
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SdFakeSetup
{
    class Paths
    {
        public string AppDir, StartMenuDir, DesktopDir;
        public bool UseRegistry = true;

        public string AppExe { get { return Path.Combine(AppDir, "SdFake.exe"); } }
        public string Uninstaller { get { return Path.Combine(AppDir, "Desinstalar.exe"); } }
        public string StartMenuLink { get { return Path.Combine(StartMenuDir, "SdFake.lnk"); } }
        public string DesktopLink { get { return Path.Combine(DesktopDir, "SdFake.lnk"); } }

        public static Paths Default()
        {
            Paths p = new Paths();
            p.AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\SdFake");
            p.StartMenuDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            p.DesktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return p;
        }

        public static Paths Test(string dir)
        {
            Paths p = new Paths();
            p.AppDir = Path.Combine(dir, "app");
            p.StartMenuDir = Path.Combine(dir, "menu");
            p.DesktopDir = Path.Combine(dir, "desktop");
            p.UseRegistry = false;
            return p;
        }
    }

    static class Installer
    {
        public const string Version = "1.0.1";
        const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SdFake";

        public static void Install(Paths p, bool desktopShortcut)
        {
            Directory.CreateDirectory(p.AppDir);
            using (Stream src = Assembly.GetExecutingAssembly().GetManifestResourceStream("SdFake.exe"))
            {
                if (src == null) throw new InvalidOperationException("Instalador incompleto: programa não encontrado dentro do instalador.");
                try
                {
                    using (FileStream dst = new FileStream(p.AppExe, FileMode.Create, FileAccess.Write, FileShare.None))
                        src.CopyTo(dst);
                }
                catch (IOException)
                {
                    throw new InvalidOperationException("Não foi possível atualizar o SdFake porque ele está aberto. Feche o programa e tente de novo.");
                }
            }

            string self = Assembly.GetExecutingAssembly().Location;
            if (!string.Equals(Path.GetFullPath(self), Path.GetFullPath(p.Uninstaller), StringComparison.OrdinalIgnoreCase))
                File.Copy(self, p.Uninstaller, true);

            Directory.CreateDirectory(p.StartMenuDir);
            CreateShortcut(p.StartMenuLink, p.AppExe, p.AppDir, "Detector de cartão SD e pendrive falso");
            if (desktopShortcut)
            {
                Directory.CreateDirectory(p.DesktopDir);
                CreateShortcut(p.DesktopLink, p.AppExe, p.AppDir, "Detector de cartão SD e pendrive falso");
            }

            if (p.UseRegistry)
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RegKey))
                {
                    k.SetValue("DisplayName", "SdFake - Detector de cartão SD e pendrive falso");
                    k.SetValue("DisplayVersion", Version);
                    k.SetValue("Publisher", "SdFake");
                    k.SetValue("DisplayIcon", p.AppExe + ",0");
                    k.SetValue("InstallLocation", p.AppDir);
                    k.SetValue("UninstallString", "\"" + p.Uninstaller + "\" /uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    long kb = (new FileInfo(p.AppExe).Length + new FileInfo(p.Uninstaller).Length) / 1024;
                    k.SetValue("EstimatedSize", (int)kb, RegistryValueKind.DWord);
                }
            }
        }

        /// <summary>Remove somente o que o instalador criou. A pasta so e apagada se ficar vazia.</summary>
        public static void Uninstall(Paths p)
        {
            TryDelete(p.StartMenuLink);
            TryDelete(p.DesktopLink);
            for (int i = 0; i < 40; i++)
            {
                TryDelete(p.AppExe);
                TryDelete(p.Uninstaller);
                if (!File.Exists(p.AppExe) && !File.Exists(p.Uninstaller)) break;
                Thread.Sleep(250);
            }
            try
            {
                if (Directory.Exists(p.AppDir) && Directory.GetFileSystemEntries(p.AppDir).Length == 0) Directory.Delete(p.AppDir);
            }
            catch (Exception) { }
            if (p.UseRegistry)
            {
                try { Registry.CurrentUser.DeleteSubKey(RegKey, false); } catch (Exception) { }
            }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }

        static void CreateShortcut(string linkPath, string target, string workDir, string description)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
                try
                {
                    Type lt = link.GetType();
                    lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
                    lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { workDir });
                    lt.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { description });
                    lt.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { target + ",0" });
                    lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                }
                finally { Marshal.ReleaseComObject(link); }
            }
            finally { Marshal.ReleaseComObject(shell); }
        }
    }

    class SetupForm : Form
    {
        readonly Paths paths = Paths.Default();
        CheckBox cbDesktop, cbRun;
        Button btInstall, btCancel;

        public SetupForm()
        {
            SuspendLayout();
            Text = "Instalar o SdFake";
            Font = new Font("Segoe UI", 9.75f);
            BackColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(520, 318);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            Panel header = new Panel();
            header.BackColor = Color.FromArgb(15, 23, 42);
            header.SetBounds(0, 0, 520, 78);
            Label title = new Label();
            title.Text = "SdFake";
            title.Font = new Font("Segoe UI", 18f, FontStyle.Bold);
            title.ForeColor = Color.White;
            title.SetBounds(20, 10, 480, 36);
            Label sub = new Label();
            sub.Text = "Detector de cartão SD e pendrive falso  ·  versão " + Installer.Version;
            sub.ForeColor = Color.FromArgb(148, 163, 184);
            sub.SetBounds(22, 46, 480, 22);
            header.Controls.Add(title);
            header.Controls.Add(sub);
            Controls.Add(header);

            Label info = new Label();
            info.Text = "O SdFake será instalado só para o seu usuário, sem precisar de permissão de administrador, em:\n\n" + paths.AppDir;
            info.SetBounds(22, 96, 476, 84);
            Controls.Add(info);

            cbDesktop = new CheckBox();
            cbDesktop.Text = "Criar atalho na Área de Trabalho";
            cbDesktop.Checked = true;
            cbDesktop.SetBounds(24, 186, 470, 24);
            Controls.Add(cbDesktop);

            cbRun = new CheckBox();
            cbRun.Text = "Abrir o SdFake ao terminar";
            cbRun.Checked = true;
            cbRun.SetBounds(24, 214, 470, 24);
            Controls.Add(cbRun);

            btInstall = new Button();
            btInstall.Text = "Instalar";
            btInstall.SetBounds(284, 268, 104, 32);
            btInstall.Click += delegate { DoInstall(); };
            Controls.Add(btInstall);

            btCancel = new Button();
            btCancel.Text = "Cancelar";
            btCancel.SetBounds(396, 268, 104, 32);
            btCancel.Click += delegate { Close(); };
            Controls.Add(btCancel);

            AcceptButton = btInstall;
            CancelButton = btCancel;
            ResumeLayout(false);
            PerformLayout();
        }

        void DoInstall()
        {
            btInstall.Enabled = false;
            Cursor = Cursors.WaitCursor;
            try
            {
                Installer.Install(paths, cbDesktop.Checked);
            }
            catch (Exception e)
            {
                Cursor = Cursors.Default;
                btInstall.Enabled = true;
                MessageBox.Show(this, "A instalação falhou:\n\n" + e.Message, "SdFake", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Cursor = Cursors.Default;
            MessageBox.Show(this, "SdFake instalado.\n\nVocê encontra o programa no Menu Iniciar"
                + (cbDesktop.Checked ? " e na Área de Trabalho." : "."), "SdFake", MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (cbRun.Checked)
            {
                try { Process.Start(paths.AppExe); } catch (Exception) { }
            }
            Close();
        }
    }

    static class Program
    {
        static string Arg(string[] args, string prefix)
        {
            foreach (string a in args)
                if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return a.Substring(prefix.Length).Trim('"');
            return null;
        }

        static bool Has(string[] args, string flag)
        {
            foreach (string a in args)
                if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                string testDir = Arg(args, "/TESTDIR=");
                if (testDir != null)
                {
                    Paths tp = Paths.Test(Path.GetFullPath(testDir));
                    if (Has(args, "/UNINSTALL")) Installer.Uninstall(tp); else Installer.Install(tp, true);
                    return 0;
                }

                if (Has(args, "/uninstall2"))
                {
                    // segunda etapa: rodando a partir de uma copia temporaria, ja pode apagar a pasta do programa
                    Thread.Sleep(600);
                    Installer.Uninstall(Paths.Default());
                    MessageBox.Show("O SdFake foi removido do computador.", "SdFake", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    SelfDelete();
                    return 0;
                }

                if (Has(args, "/uninstall"))
                {
                    if (MessageBox.Show("Remover o SdFake deste computador?", "Desinstalar o SdFake",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 1;
                    string temp = Path.Combine(Path.GetTempPath(), "SdFake-Desinstalar-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".exe");
                    File.Copy(Assembly.GetExecutingAssembly().Location, temp, true);
                    ProcessStartInfo psi = new ProcessStartInfo(temp, "/uninstall2");
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                    return 0;
                }

                if (Has(args, "/S"))
                {
                    Installer.Install(Paths.Default(), true);
                    return 0;
                }

                Application.Run(new SetupForm());
                return 0;
            }
            catch (Exception e)
            {
                if (Arg(args, "/TESTDIR=") != null || Has(args, "/S"))
                {
                    try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "SdFake-instalador-erro.txt"), e.ToString()); } catch (Exception) { }
                    return 2;
                }
                MessageBox.Show("Erro: " + e.Message, "SdFake", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
        }

        /// <summary>Apaga a copia temporaria do desinstalador depois que ela terminar.</summary>
        static void SelfDelete()
        {
            try
            {
                string self = Assembly.GetExecutingAssembly().Location;
                if (!self.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) return;
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 >nul & del /q \"" + self + "\"");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
            }
            catch (Exception) { }
        }
    }
}
