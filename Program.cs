using System.Text;

namespace LodeonAdmin;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // Los modos de consola se resuelven ANTES de inicializar WinForms: en un
        // ejecutable autocontenido, ApplicationConfiguration.Initialize puede
        // consumir el flujo y dejar el proceso sin ejecutar el comando.
        if (args.Length > 0)
        {
            int? code = null;
            try
            {
                switch (args[0].TrimStart('-', '/').ToLowerInvariant())
                {
                    case "selftest": code = SelfTest.Run(); break;
                    case "langreport": code = LangReport.Run(); break;
                    case "bridge": code = BridgeCli.Run(args); break;
                    case "sortreport": code = SortReport.Run(); break;
                    case "configcheck": code = ConfigCheck.Run(); break;
                    case "health": code = HealthReport.Run(); break;
                    case "detect": code = DetectReport.Run(); break;
                }
            }
            catch (Exception ex)
            {
                try
                {
                    var log = Path.Combine(AppContext.BaseDirectory, "error.txt");
                    File.WriteAllText(log, ex.ToString(), Encoding.UTF8);
                }
                catch { }
                code = 2;
            }

            if (code.HasValue)
            {
                Environment.Exit(code.Value);
                return;
            }
        }

        ApplicationConfiguration.Initialize();

        // -page X abre la app directamente en una pagina: permite verificar el
        // diseno de cada pantalla sin depender de clics automatizados.
        string? page = null;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-page") page = args[i + 1];

        var f = new MainForm();
        if (page != null) f.StartPage = page;
        Application.Run(f);
    }
}
