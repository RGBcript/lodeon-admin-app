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
                    case "ordercheck": code = OrderCheck.Run(); break;
                    case "versiones": code = VersionsReport.Run(); break;
                    case "coleccion": code = CollectionReport.Run(args); break;
                    case "vincular": code = VincularReport.Run(args); break;

                    // -page <nombre> abre la ventana en una pagina concreta: no es un
                    // comando de consola, asi que se deja pasar a la interfaz.
                    case "page": break;

                    // Un argumento desconocido NO debe abrir la ventana: en un script
                    // eso cuelga la ejecucion sin decir nada. Se avisa y se listan los
                    // comandos validos.
                    default:
                        // Console.OutputEncoding lanza si la salida esta redirigida:
                        // sin try/catch el catch de abajo devolvia 2 en vez de 3.
                        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
                        var err = Console.Error;
                        err.WriteLine("Comando desconocido: " + args[0]);
                        err.WriteLine();
                        err.WriteLine("Comandos disponibles:");
                        err.WriteLine("  -selftest      rutas, archivos y configuración");
                        err.WriteLine("  -detect        qué juego y SteamCMD se detectan");
                        err.WriteLine("  -versiones     instalaciones encontradas");
                        err.WriteLine("  -health        informe del registro de errores");
                        err.WriteLine("  -sortreport    comprobación del orden de mods");
                        err.WriteLine("  -ordercheck    posiciones del orden de carga");
                        err.WriteLine("  -configcheck   estado de ModsConfig.xml");
                        err.WriteLine("  -langreport    idiomas detectados");
                        err.WriteLine("  -bridge        herramientas de traducción");
                        err.WriteLine();
                        err.WriteLine("Sin argumentos se abre la ventana; -page <nombre> en una página concreta.");
                        code = 3;
                        break;
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
