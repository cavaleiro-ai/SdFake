using System;
using System.Windows.Forms;

namespace SdFake
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Native.SetErrorMode(1);   // sem caixas "nao ha disco na unidade" ao consultar leitores vazios
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // --quick E:      usado pelo proprio programa ao reabrir como administrador
            // --pasta <dir>   diagnostico: oferece uma pasta comum como alvo do Teste Completo
            // --limite <MiB>  diagnostico: limita quanto o Teste Completo grava
            string quick = null, folder = null;
            long limit = 0;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "--quick") quick = args[i + 1];
                else if (args[i] == "--pasta") folder = args[i + 1];
                else if (args[i] == "--limite") long.TryParse(args[i + 1], out limit);
            }
            // Boas-vindas ao abrir. Nao aparece quando o programa reabre sozinho como administrador
            // para continuar um teste, nem no modo de diagnostico.
            if (quick == null && folder == null)
            {
                using (WelcomeForm welcome = new WelcomeForm()) welcome.ShowDialog();
            }
            Application.Run(new MainForm(quick, folder, Math.Max(0, limit)));
        }
    }
}
