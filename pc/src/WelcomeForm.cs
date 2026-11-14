// SdFake - tela de boas-vindas, mostrada ao abrir o programa.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace SdFake
{
    /// <summary>Enderecos do desenvolvedor (os mesmos do app Android, em res/values/strings.xml).</summary>
    static class Developer
    {
        public const string Handle = "@cavaleiro.ai";
        public const string InstagramUrl = "https://www.instagram.com/cavaleiro.ai/";
        public const string TikTokUrl = "https://www.tiktok.com/@cavaleiro.ai";
        public const string SiteLabel = "www.cavaleiro.ai";
        public const string SiteUrl = "https://www.cavaleiro.ai";
    }

    public class WelcomeForm : Form
    {
        public WelcomeForm()
        {
            SuspendLayout();
            Text = "SdFake";
            Font = new Font("Segoe UI", 9.75f);
            BackColor = Ui.Page;
            ForeColor = Ui.Ink;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(520, 486);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }

            Header header = new Header();
            header.Title = "Bem-vindo ao SdFake!";
            header.Subtitle = "Detector de cartão SD e pendrive falso";
            header.SetBounds(0, 0, 520, 84);
            try
            {
                System.IO.Stream logo = typeof(WelcomeForm).Assembly.GetManifestResourceStream("logo.png");
                if (logo != null) header.Logo = Image.FromStream(logo);
            }
            catch (Exception) { }
            Controls.Add(header);

            Label intro = new Label();
            intro.Text = "Obrigado por usar o SdFake. Em poucos minutos você descobre se o seu cartão SD ou pendrive "
                + "tem mesmo a capacidade que está escrita nele.";
            intro.SetBounds(24, 100, 472, 46);
            intro.UseMnemonic = false;
            Controls.Add(intro);

            Label follow = new Label();
            follow.Text = "Gostou? Siga o desenvolvedor e acompanhe as novidades:";
            follow.Font = new Font(Font, FontStyle.Bold);
            follow.SetBounds(24, 154, 472, 22);
            follow.UseMnemonic = false;
            Controls.Add(follow);

            AddLink("Instagram", Developer.Handle, Developer.InstagramUrl, Color.FromArgb(225, 48, 108), 184);
            AddLink("TikTok", Developer.Handle, Developer.TikTokUrl, Color.FromArgb(17, 17, 17), 256);
            AddLink("Site", Developer.SiteLabel, Developer.SiteUrl, Ui.Blue, 328);

            Button start = new Button();
            start.Text = "Começar";
            start.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
            start.FlatStyle = FlatStyle.Flat;
            start.FlatAppearance.BorderSize = 0;
            start.BackColor = Ui.Blue;
            start.ForeColor = Color.White;
            start.UseVisualStyleBackColor = false;
            start.SetBounds(24, 418, 472, 46);
            start.Click += delegate { Close(); };
            Controls.Add(start);

            AcceptButton = start;
            CancelButton = start;
            ResumeLayout(false);
            PerformLayout();
            Shown += delegate { start.Focus(); };
        }

        void AddLink(string title, string description, string url, Color accent, int top)
        {
            ChoiceButton b = new ChoiceButton();
            b.Title = title;
            b.Description = description;
            b.Accent = accent;
            b.Cursor = Cursors.Hand;
            b.SetBounds(24, top, 472, 64);
            b.Click += delegate { Open(url); };
            Controls.Add(b);
        }

        /// <summary>Abre o endereco no navegador padrao.</summary>
        void Open(string url)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(url);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception)
            {
                MessageBox.Show(this, "Não foi possível abrir o navegador. Endereço:\n\n" + url, "SdFake",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
