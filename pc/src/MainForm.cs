// SdFake - janela principal.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Media;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace SdFake
{
    static class Ui
    {
        public static readonly Color Navy = Color.FromArgb(15, 23, 42);
        public static readonly Color Blue = Color.FromArgb(37, 99, 235);
        public static readonly Color Ink = Color.FromArgb(30, 41, 59);
        public static readonly Color Muted = Color.FromArgb(100, 116, 139);
        public static readonly Color Line = Color.FromArgb(203, 213, 225);
        public static readonly Color Page = Color.FromArgb(248, 250, 252);

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = radius * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>Botao grande com titulo e descricao.</summary>
    class ChoiceButton : Button
    {
        string title = "";
        public string Description = "";
        public Color Accent = Ui.Blue;
        bool hover, down;

        /// <summary>O titulo tambem vira o texto do controle (leitores de tela e automacao).</summary>
        public string Title
        {
            get { return title; }
            set { title = value; Text = value; AccessibleName = value; Invalidate(); }
        }

        public ChoiceButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = g.DpiX / 96f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Page);

            RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill = !Enabled ? Color.FromArgb(241, 245, 249)
                       : down ? Color.FromArgb(219, 234, 254)
                       : hover ? Color.FromArgb(239, 246, 255) : Color.White;
            Color border = !Enabled ? Ui.Line : (hover || Focused) ? Accent : Ui.Line;
            using (GraphicsPath p = Ui.Rounded(r, 8 * s))
            {
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(border, (hover || Focused) && Enabled ? 2f * s : 1f * s)) g.DrawPath(pen, p);
            }
            using (GraphicsPath bar = Ui.Rounded(new RectangleF(10 * s, 14 * s, 5 * s, Height - 28 * s), 2.5f * s))
            using (SolidBrush b = new SolidBrush(Enabled ? Accent : Ui.Line))
                g.FillPath(b, bar);

            Color titleColor = Enabled ? Ui.Ink : Ui.Muted;
            using (Font tf = new Font(Font.FontFamily, 12.5f, FontStyle.Bold))
            {
                Rectangle tr = new Rectangle((int)(26 * s), (int)(11 * s), Width - (int)(36 * s), (int)(26 * s));
                TextRenderer.DrawText(g, Title, tf, tr, titleColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            Rectangle dr = new Rectangle((int)(26 * s), (int)(39 * s), Width - (int)(38 * s), Height - (int)(45 * s));
            TextRenderer.DrawText(g, Description, Font, dr, Ui.Muted, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Faixa colorida com o veredito.</summary>
    class Banner : Control
    {
        public string Title = "";
        public string Summary = "";
        public Color Fill = Color.FromArgb(226, 232, 240);
        public Color Fore = Ui.Ink;

        public Banner()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void Set(string title, string summary, Color fill, Color fore)
        {
            Title = title; Summary = summary; Fill = fill; Fore = fore;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = g.DpiX / 96f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Ui.Page);
            using (GraphicsPath p = Ui.Rounded(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8 * s))
            using (SolidBrush b = new SolidBrush(Fill))
                g.FillPath(b, p);

            int pad = (int)(16 * s);
            int top = (int)(9 * s);
            int titleH = 0;
            if (Title.Length > 0)
            {
                using (Font tf = new Font(Font.FontFamily, 17f, FontStyle.Bold))
                {
                    titleH = (int)(32 * s);
                    TextRenderer.DrawText(g, Title, tf, new Rectangle(pad, top, Width - 2 * pad, titleH), Fore,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
            Rectangle sr = new Rectangle(pad, top + titleH + (int)(2 * s), Width - 2 * pad, Height - top - titleH - (int)(8 * s));
            TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
            flags |= Title.Length > 0 ? TextFormatFlags.Top : TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, Summary, Font, sr, Fore, flags);
        }
    }

    class Header : Control
    {
        public Image Logo;
        public string Title = "SdFake";
        public string Subtitle = "Descubra se o cartão SD ou pendrive tem mesmo a capacidade anunciada";

        public Header()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float s = g.DpiX / 96f;
            g.Clear(Ui.Navy);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int x = (int)(20 * s);
            if (Logo != null)
            {
                int size = (int)(52 * s);
                g.DrawImage(Logo, new Rectangle(x, (Height - size) / 2, size, size));
                x += size + (int)(14 * s);
            }
            using (Font tf = new Font(Font.FontFamily, 19f, FontStyle.Bold))
                TextRenderer.DrawText(g, Title, tf, new Point(x - (int)(3 * s), (int)(12 * s)), Color.White, TextFormatFlags.NoPrefix);
            using (Font sf = new Font(Font.FontFamily, 10.5f))
                TextRenderer.DrawText(g, Subtitle, sf,
                    new Point(x, (int)(48 * s)), Color.FromArgb(148, 163, 184), TextFormatFlags.NoPrefix);
        }
    }

    public class MainForm : Form
    {
        readonly string autoQuickLetter;
        readonly string extraFolder;
        readonly long limitBytes;
        ComboBox cbDrive;
        Button btRefresh, btCancel;
        Label lbInfo, lbPhase, lbDetail;
        ChoiceButton btQuick, btFull;
        ProgressBar bar;
        Banner banner;
        TextBox tbLog;
        System.Windows.Forms.Timer uiTimer, deviceTimer;

        Thread worker;
        FullTest full;
        QuickTest quick;
        bool running;
        bool closeWhenDone;
        volatile Progress latest;
        QuickResult lastQuick;
        string lastQuickLetter;

        /// <param name="extraFolder">Opcional (diagnostico): pasta comum oferecida como alvo do Teste Completo.</param>
        /// <param name="limitMiB">Opcional (diagnostico): limita quanto o Teste Completo grava.</param>
        public MainForm(string autoQuickLetter, string extraFolder, long limitMiB)
        {
            this.autoQuickLetter = autoQuickLetter;
            this.extraFolder = extraFolder;
            this.limitBytes = limitMiB * 1024L * 1024L;
            Build();
        }

        Label MakeLabel(string text, int x, int y, int w, int h, bool bold, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.SetBounds(x, y, w, h);
            l.ForeColor = color;
            l.AutoEllipsis = true;
            l.UseMnemonic = false;
            if (bold) l.Font = new Font(Font, FontStyle.Bold);
            Controls.Add(l);
            return l;
        }

        void Build()
        {
            SuspendLayout();
            Text = "SdFake";
            Font = new Font("Segoe UI", 9.75f);
            BackColor = Ui.Page;
            ForeColor = Ui.Ink;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(720, 652);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            Header header = new Header();
            header.SetBounds(0, 0, 720, 84);
            try
            {
                System.IO.Stream logo = typeof(MainForm).Assembly.GetManifestResourceStream("logo.png");
                if (logo != null) header.Logo = Image.FromStream(logo);
            }
            catch (Exception) { }
            Controls.Add(header);

            MakeLabel("1.  Escolha o dispositivo", 20, 98, 400, 22, true, Ui.Ink);
            cbDrive = new ComboBox();
            cbDrive.DropDownStyle = ComboBoxStyle.DropDownList;
            cbDrive.SetBounds(20, 123, 566, 28);
            cbDrive.SelectedIndexChanged += delegate { UpdateSelection(); };
            Controls.Add(cbDrive);

            btRefresh = new Button();
            btRefresh.Text = "Atualizar";
            btRefresh.SetBounds(596, 122, 104, 28);
            btRefresh.Click += delegate { RefreshDrives(); };
            Controls.Add(btRefresh);

            lbInfo = MakeLabel("", 20, 155, 680, 22, false, Ui.Muted);

            MakeLabel("2.  Escolha o teste", 20, 188, 400, 22, true, Ui.Ink);
            btQuick = new ChoiceButton();
            btQuick.Title = "Teste Rápido";
            btQuick.Description = "1 a 5 minutos. Confere cerca de 500 pontos espalhados por todo o dispositivo. Mantém seus arquivos. Pede permissão de administrador.";
            btQuick.SetBounds(20, 213, 335, 104);
            btQuick.Click += delegate { StartQuick(false); };
            Controls.Add(btQuick);

            btFull = new ChoiceButton();
            btFull.Title = "Teste Completo";
            btFull.Accent = Color.FromArgb(13, 148, 136);
            btFull.Description = "Preenche o espaço livre e confere cada byte. Falso aparece em minutos; verdadeiro e grande pode levar horas. Mantém seus arquivos.";
            btFull.SetBounds(365, 213, 335, 104);
            btFull.Click += delegate { StartFull(); };
            Controls.Add(btFull);

            lbPhase = MakeLabel("", 20, 332, 566, 22, true, Ui.Ink);
            btCancel = new Button();
            btCancel.Text = "Cancelar";
            btCancel.SetBounds(596, 328, 104, 28);
            btCancel.Enabled = false;
            btCancel.Visible = false;
            btCancel.Click += delegate { CancelRun(); };
            Controls.Add(btCancel);

            bar = new ProgressBar();
            bar.SetBounds(20, 360, 680, 16);
            bar.Maximum = 1000;
            Controls.Add(bar);
            lbDetail = MakeLabel("", 20, 380, 680, 22, false, Ui.Muted);

            banner = new Banner();
            banner.SetBounds(20, 410, 680, 96);
            Controls.Add(banner);

            tbLog = new TextBox();
            tbLog.Multiline = true;
            tbLog.ReadOnly = true;
            tbLog.ScrollBars = ScrollBars.Vertical;
            tbLog.BackColor = Color.White;
            tbLog.ForeColor = Ui.Ink;
            tbLog.BorderStyle = BorderStyle.FixedSingle;
            tbLog.SetBounds(20, 516, 680, 116);
            tbLog.TabStop = false;
            Controls.Add(tbLog);

            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 120;
            uiTimer.Tick += delegate { ShowLatestProgress(); };

            deviceTimer = new System.Windows.Forms.Timer();
            deviceTimer.Interval = 900;
            deviceTimer.Tick += delegate { deviceTimer.Stop(); if (!running) RefreshDrives(); };

            ResumeLayout(false);
            PerformLayout();

            Idle();
            Load += delegate { RefreshDrives(); };
            Shown += delegate { AutoStart(); };
            FormClosing += OnClosing;
        }

        void Idle()
        {
            banner.Set("", "Conecte o cartão SD ou pendrive, escolha o dispositivo na lista e clique em um dos testes.",
                Color.FromArgb(226, 232, 240), Ui.Ink);
        }

        // ------------------------------------------------------------------ dispositivos

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == 0x0219 && deviceTimer != null)   // WM_DEVICECHANGE
            {
                deviceTimer.Stop();
                deviceTimer.Start();
            }
        }

        DriveEntry Selected()
        {
            return cbDrive.SelectedItem as DriveEntry;
        }

        void RefreshDrives()
        {
            string keep = Selected() != null ? Selected().Letter : null;
            List<DriveEntry> list;
            Cursor = Cursors.WaitCursor;
            try
            {
                list = DriveCatalog.List();
                if (extraFolder != null)
                {
                    DriveEntry folder = DriveCatalog.Folder(extraFolder);
                    if (folder != null) list.Insert(0, folder);
                }
            }
            finally { Cursor = Cursors.Default; }

            cbDrive.BeginUpdate();
            cbDrive.Items.Clear();
            int select = -1;
            for (int i = 0; i < list.Count; i++)
            {
                cbDrive.Items.Add(list[i]);
                if (keep != null && list[i].Letter == keep) select = i;
            }
            if (select < 0)
            {
                // nunca pre-seleciona um HD/SSD externo: prioriza cartao ou pendrive
                for (int i = 0; i < list.Count && select < 0; i++) if (list[i].Removable && list[i].HasMedia) select = i;
                for (int i = 0; i < list.Count && select < 0; i++) if (list[i].Removable) select = i;
            }
            cbDrive.EndUpdate();
            cbDrive.Enabled = list.Count > 0 && !running;
            cbDrive.SelectedIndex = select;
            UpdateSelection();
        }

        void UpdateSelection()
        {
            DriveEntry d = Selected();
            if (d == null)
            {
                lbInfo.Text = cbDrive.Items.Count > 0
                    ? "Conecte o cartão ou pendrive (a lista atualiza sozinha) ou escolha um dispositivo na lista."
                    : "Nenhum cartão ou pendrive encontrado. Conecte o dispositivo: a lista atualiza sozinha.";
            }
            else if (d.IsPartial)
            {
                lbInfo.Text = "⚠ Anuncia " + Fmt.Bytes(d.DeviceBytes) + ", mas está formatado com só " + Fmt.Bytes(d.Total)
                    + ". Use o Teste Rápido: ele examina o dispositivo inteiro.";
            }
            else if (d.Ready)
            {
                string model = d.Model.Length > 0 ? d.Model + "  ·  " : "";
                lbInfo.Text = model + d.FileSystem + "  ·  capacidade anunciada " + Fmt.Bytes(d.Total) + "  ·  livre " + Fmt.Bytes(d.Free);
            }
            else if (d.MediaBytes > 0)
            {
                lbInfo.Text = "Sem formatação reconhecida: só o Teste Rápido está disponível (para o Completo, formate o cartão antes).";
            }
            else
            {
                lbInfo.Text = "Leitor vazio. Insira o cartão: a lista atualiza sozinha.";
            }
            lbInfo.ForeColor = d != null && d.IsPartial ? Color.FromArgb(180, 83, 9) : Ui.Muted;
            btQuick.Enabled = !running && d != null && d.HasMedia && !d.IsFolder;
            btFull.Enabled = !running && d != null && d.Ready;

            // O resultado guardado do Teste Rapido so vale enquanto o mesmo dispositivo continuar conectado.
            if (lastQuick != null && !running)
            {
                DriveEntry same = null;
                foreach (object item in cbDrive.Items)
                {
                    DriveEntry e = item as DriveEntry;
                    if (e != null && e.Letter == lastQuickLetter) same = e;
                }
                if (same == null || !same.HasMedia || !SameSize(same.DeviceBytes, lastQuick.DeclaredBytes)) lastQuick = null;
            }
        }

        static bool SameSize(long a, long b)
        {
            return a > 0 && b > 0 && Math.Abs(a - b) <= a / 100;
        }

        /// <summary>Resultado do Teste Rapido desta sessao para o mesmo dispositivo, ou null.</summary>
        QuickResult QuickResultFor(string letter, long deviceBytes)
        {
            if (lastQuick == null || lastQuickLetter != letter || !SameSize(deviceBytes, lastQuick.DeclaredBytes)) return null;
            return lastQuick;
        }

        // ------------------------------------------------------------------ execucao

        static bool IsAdmin()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch (Exception) { return false; }
        }

        void Log(string line)
        {
            if (tbLog.TextLength > 0) tbLog.AppendText(Environment.NewLine);
            tbLog.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
        }

        void Post(MethodInvoker action)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(action); }
            catch (Exception) { }
        }

        void BeginRun(string what)
        {
            running = true;
            latest = null;
            cbDrive.Enabled = false;
            btRefresh.Enabled = false;
            btQuick.Enabled = false;
            btFull.Enabled = false;
            btCancel.Enabled = true;
            btCancel.Visible = true;
            bar.Value = 0;
            lbPhase.Text = "Preparando...";
            lbDetail.Text = "";
            banner.Set("", what + " em andamento. Não remova o dispositivo.", Color.FromArgb(219, 234, 254), Color.FromArgb(30, 58, 138));
            Log(what + " iniciado.");
            uiTimer.Start();
        }

        void EndRun()
        {
            running = false;
            uiTimer.Stop();
            btRefresh.Enabled = true;
            btCancel.Enabled = false;
            btCancel.Visible = false;
            Text = "SdFake";
            full = null;
            quick = null;
            worker = null;
        }

        void ShowLatestProgress()
        {
            Progress p = latest;
            if (p == null) return;
            int v = (int)(p.Fraction * 1000);
            bar.Value = Math.Max(0, Math.Min(1000, v));
            if (btCancel.Enabled) lbPhase.Text = p.Phase + "  —  " + Fmt.Percent(p.Fraction);
            lbDetail.Text = p.Detail;
            Text = "SdFake  —  " + Fmt.Percent(p.Fraction);
        }

        void CancelRun()
        {
            if (!running) return;
            FullTest f = full;
            QuickTest q = quick;
            if (f != null) f.CancelRequested = true;
            if (q != null) q.CancelRequested = true;
            btCancel.Enabled = false;
            lbPhase.Text = "Cancelando e limpando... aguarde.";
        }

        void Finish(ReportText t)
        {
            EndRun();
            Color fill, fore;
            string mark;
            switch (t.Verdict)
            {
                case Verdict.Genuine: fill = Color.FromArgb(220, 252, 231); fore = Color.FromArgb(22, 101, 52); mark = "✔  "; break;
                case Verdict.Fake: fill = Color.FromArgb(254, 226, 226); fore = Color.FromArgb(153, 27, 27); mark = "✖  "; break;
                case Verdict.Defective: fill = Color.FromArgb(254, 243, 199); fore = Color.FromArgb(146, 64, 14); mark = "⚠  "; break;
                case Verdict.Incomplete: fill = Color.FromArgb(226, 232, 240); fore = Ui.Ink; mark = ""; break;
                case Verdict.Partial: fill = Color.FromArgb(254, 243, 199); fore = Color.FromArgb(146, 64, 14); mark = "⚠  "; break;
                default: fill = Color.FromArgb(254, 243, 199); fore = Color.FromArgb(146, 64, 14); mark = ""; break;
            }
            banner.Set(mark + t.Title, t.Summary, fill, fore);
            lbPhase.Text = "Resultado: " + t.Title;
            lbDetail.Text = "";
            if (t.Verdict == Verdict.Genuine || t.Verdict == Verdict.Fake || t.Verdict == Verdict.Defective || t.Verdict == Verdict.Partial) bar.Value = 1000;
            Log("RESULTADO: " + t.Title + " — " + t.Summary);
            foreach (string line in t.Lines) Log("   " + line);
            try
            {
                if (t.Verdict == Verdict.Genuine) SystemSounds.Asterisk.Play();
                else if (t.Verdict == Verdict.Incomplete) { }
                else SystemSounds.Exclamation.Play();
            }
            catch (Exception) { }
            if (closeWhenDone) Close(); else RefreshDrives();
        }

        static ReportText ErrorReport(string message)
        {
            ReportText t = new ReportText();
            t.Verdict = Verdict.Error;
            t.Title = "ERRO";
            t.Summary = message;
            return t;
        }

        /// <summary>HD/SSD externo nao e cartao nem pendrive: pede uma confirmacao a mais, com o padrao em "Nao".</summary>
        bool ConfirmExternalDisk(DriveEntry d)
        {
            if (d.Removable) return true;
            string name = d.Label.Length > 0 ? d.Label : d.Model;
            string msg = "ATENÇÃO: " + d.Letter + " (" + name + ", " + Fmt.Bytes(d.Total) + ") é um disco USB externo, não um cartão de memória nem um pendrive.\n\n"
                + "Só teste este disco se a suspeita de capacidade falsa for dele mesmo. "
                + "Se ele guarda seus arquivos ou backups, escolha \"Não\".\n\nTestar este disco mesmo assim?";
            return MessageBox.Show(this, msg, "Disco externo", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        void StartFull()
        {
            if (running) return;
            DriveEntry d = Selected();
            if (d == null || !d.Ready) return;
            if (!ConfirmExternalDisk(d)) return;

            // Volume bem menor que o dispositivo anuncia: o Teste Completo nao alcanca o resto.
            if (d.IsPartial && QuickResultFor(d.Letter, d.DeviceBytes) == null)
            {
                string warn = "ATENÇÃO: este dispositivo anuncia " + Fmt.Bytes(d.DeviceBytes) + ", mas está formatado com apenas " + Fmt.Bytes(d.Total) + ".\n\n"
                    + "O Teste Completo só consegue conferir a parte formatada. Para saber se os " + Fmt.Bytes(d.DeviceBytes)
                    + " existem de verdade, o teste certo é o Teste Rápido, que examina o dispositivo inteiro.\n\n"
                    + "Sim  =  rodar o Teste Rápido (recomendado)\n"
                    + "Não  =  rodar o Teste Completo só na parte formatada";
                DialogResult choice = MessageBox.Show(this, warn, "Dispositivo formatado com menos do que anuncia",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (choice == DialogResult.Cancel) return;
                if (choice == DialogResult.Yes) { StartQuick(false); return; }
            }

            string msg = "O Teste Completo grava arquivos de teste em todo o espaço livre de " + d.Letter + " (" + Fmt.Bytes(d.Free) + ") e depois confere tudo.\n\n"
                + "•  Seus arquivos NÃO são apagados; os arquivos de teste são removidos no final.\n"
                + "•  Cartão falso costuma ser desmascarado em poucos minutos.\n"
                + "•  Cartão verdadeiro e grande pode levar horas (depende da velocidade dele).\n"
                + "•  Não remova o dispositivo durante o teste.\n\n"
                + "Dica: quanto mais vazio o cartão, mais completa é a conferência.\n\nComeçar?";
            if (MessageBox.Show(this, msg, "Teste Completo", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;

            FullTest test = new FullTest();
            test.LimitBytes = limitBytes;
            test.DeviceBytes = Math.Max(0, d.DeviceBytes);
            test.OnProgress = delegate(Progress p) { latest = p; };
            test.OnLog = delegate(string s) { Post(delegate { Log(s); }); };
            full = test;
            BeginRun("Teste Completo em " + d.Letter);
            string root = d.Root;
            string letter = d.Letter;
            worker = new Thread(delegate()
            {
                FullResult result = null;
                ReportText report = null;
                try
                {
                    Native.KeepAwake(true);
                    result = test.Run(new WinStore(root));
                }
                catch (Exception e)
                {
                    report = ErrorReport("Erro inesperado: " + e.Message);
                }
                Post(delegate
                {
                    if (result != null) FinishFull(result, letter); else Finish(report);
                });
            });
            worker.IsBackground = true;
            worker.Start();
        }

        /// <summary>
        /// Fecha o Teste Completo. Se ele so alcancou uma parte do dispositivo (volume menor do que o
        /// dispositivo anuncia), o veredito final vem do Teste Rapido: usa o resultado ja obtido nesta
        /// sessao ou oferece roda-lo agora.
        /// </summary>
        void FinishFull(FullResult r, string letter)
        {
            ReportText t = FullTest.Describe(r);
            bool offerQuick = false;
            if (r.Verdict == Verdict.Partial)
            {
                QuickResult q = QuickResultFor(letter, r.DeviceBytes);
                if (q != null && q.Verdict == Verdict.Fake)
                {
                    t.Verdict = Verdict.Fake;
                    t.Title = "FALSO";
                    t.Summary = "Capacidade real " + (q.RealBytes >= 0 ? "aproximada: " + Fmt.Bytes(q.RealBytes) : "bem menor")
                        + "  —  anunciado: " + Fmt.Bytes(r.DeviceBytes) + ". A parte formatada (" + Fmt.Bytes(r.TotalBytes)
                        + ") funciona, mas o restante que o dispositivo anuncia não existe (Teste Rápido).";
                }
                else if (q != null && q.Verdict == Verdict.Genuine)
                {
                    t.Verdict = Verdict.Genuine;
                    t.Title = "VERDADEIRO";
                    t.Summary = "A parte formatada (" + Fmt.Bytes(r.TotalBytes) + ") foi conferida por inteiro e o Teste Rápido confirmou os "
                        + Fmt.Bytes(r.DeviceBytes) + " do dispositivo. Ele só está formatado com um tamanho menor que o total.";
                }
                else if (q == null)
                {
                    offerQuick = true;
                }
            }
            Finish(t);
            if (!offerQuick || closeWhenDone || IsDisposed) return;

            string ask = "Este dispositivo anuncia " + Fmt.Bytes(r.DeviceBytes) + ", mas está formatado com apenas " + Fmt.Bytes(r.TotalBytes)
                + ". O Teste Completo conferiu só essa parte.\n\n"
                + "Rodar agora o Teste Rápido, que examina o dispositivo inteiro (1 a 5 minutos, mantém seus arquivos)?";
            if (MessageBox.Show(this, ask, "Falta testar o restante", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            for (int i = 0; i < cbDrive.Items.Count; i++)
            {
                DriveEntry e = cbDrive.Items[i] as DriveEntry;
                if (e != null && e.Letter == letter)
                {
                    cbDrive.SelectedIndex = i;
                    StartQuick(true);
                    return;
                }
            }
        }

        void StartQuick(bool confirmed)
        {
            if (running) return;
            DriveEntry d = Selected();
            if (d == null || !d.HasMedia) return;
            if (!confirmed)
            {
                if (!ConfirmExternalDisk(d)) return;
                string msg = "O Teste Rápido grava e lê pequenos blocos em cerca de 500 pontos espalhados por todo o dispositivo " + d.Letter + ".\n\n"
                    + "•  Leva de 1 a 5 minutos.\n"
                    + "•  O conteúdo de cada ponto é guardado antes e devolvido depois, então seus arquivos são preservados. "
                    + "Mesmo assim, se houver algo importante no cartão, faça uma cópia antes.\n"
                    + "•  Feche janelas e programas que estejam usando o cartão.\n"
                    + "•  Não remova o dispositivo durante o teste.\n\nComeçar?";
                if (MessageBox.Show(this, msg, "Teste Rápido", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            }

            QuickTest test = new QuickTest();
            test.OnProgress = delegate(Progress p) { latest = p; };
            quick = test;
            BeginRun("Teste Rápido em " + d.Letter);
            string letter = d.Letter;
            worker = new Thread(delegate()
            {
                ReportText report = null;
                QuickResult result = null;
                bool needAdmin = false;
                try
                {
                    Native.KeepAwake(true);
                    using (RawDisk disk = RawDisk.Open(letter))
                    {
                        result = test.Run(disk);
                        report = QuickTest.Describe(result);
                    }
                }
                catch (TestAbortException e)
                {
                    needAdmin = e.NeedAdmin;
                    report = ErrorReport(e.Message);
                }
                catch (Exception e)
                {
                    report = ErrorReport("Erro inesperado: " + e.Message);
                }
                Post(delegate
                {
                    if (needAdmin && !IsAdmin())
                    {
                        EndRun();
                        Idle();
                        lbPhase.Text = "";
                        RefreshDrives();
                        Elevate(letter);
                    }
                    else
                    {
                        // guarda o veredito do dispositivo inteiro para o Teste Completo nao contradize-lo depois
                        Finish(report);
                        if (result != null && (result.Verdict == Verdict.Fake || result.Verdict == Verdict.Genuine))
                        {
                            lastQuick = result;
                            lastQuickLetter = letter;
                        }
                    }
                });
            });
            worker.IsBackground = true;
            worker.Start();
        }

        void Elevate(string letter)
        {
            string msg = "Para ler e gravar direto no dispositivo, o Windows exige permissão de administrador.\n\n"
                + "O SdFake vai reabrir pedindo essa permissão e iniciar o Teste Rápido em " + letter + ".\n\nContinuar?";
            if (MessageBox.Show(this, msg, "Permissão de administrador", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
            {
                Log("Teste Rápido não iniciado: permissão de administrador não concedida.");
                return;
            }
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, "--quick " + letter);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                Close();
            }
            catch (Win32Exception)
            {
                Log("Teste Rápido não iniciado: permissão de administrador não concedida.");
            }
        }

        void AutoStart()
        {
            if (string.IsNullOrEmpty(autoQuickLetter)) return;
            string letter = autoQuickLetter.Substring(0, Math.Min(2, autoQuickLetter.Length)).ToUpperInvariant();
            for (int i = 0; i < cbDrive.Items.Count; i++)
            {
                DriveEntry d = cbDrive.Items[i] as DriveEntry;
                if (d != null && d.Letter == letter)
                {
                    cbDrive.SelectedIndex = i;
                    StartQuick(true);
                    return;
                }
            }
            Log("O dispositivo " + letter + " não foi encontrado.");
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!running) return;
            e.Cancel = true;
            if (closeWhenDone) return;
            if (MessageBox.Show(this, "Há um teste em andamento. Cancelar o teste e fechar?\n\n(O programa fecha assim que terminar de limpar o dispositivo.)",
                "SdFake", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                closeWhenDone = true;
                CancelRun();
            }
        }
    }
}
