using System.Text;

namespace LodeonAdmin;

/// <summary>Comandos de idioma por consola, para probar sin interfaz.</summary>
public static class BridgeCli
{
    public static int Run(string[] args)
    {
        var sb = new StringBuilder();
        void W(string s) { sb.AppendLine(s); }

        try
        {
            var accion = args.Length > 1 ? args[1] : "estado";
            var idiomaArg = args.Length > 2 ? args[2] : null;

            var (juego, juegoFolder) = LanguageSystem.GameLanguage();
            var (det, _) = LanguageSystem.DetectUserLanguage();
            var canon = idiomaArg ?? juego;

            switch (accion)
            {
                case "estado":
                    W($"usuario: {det}   juego: {juego} (\"{juegoFolder}\")");
                    var cand0 = LanguageSystem.BridgeCandidates(canon);
                    W($"candidatos a puente para '{canon}': {cand0.Count}");
                    break;

                case "crear":
                    {
                        var cand = LanguageSystem.BridgeCandidates(canon);
                        int ok = 0, fallos = 0;
                        foreach (var c in cand)
                        {
                            if (c.Pick == null) continue;
                            var (good, msg, enlace) = LanguageSystem.CreateBridge(c.Pick, canon);
                            if (good) { ok++; W($"  OK    {c.ModName}  ({msg})"); }
                            else { fallos++; W($"  FALLO {c.ModName}: {msg}"); }
                        }
                        W($"creados: {ok}   fallos: {fallos}");
                        break;
                    }

                case "quitar":
                    {
                        var n = LanguageSystem.RemoveBridges();
                        W($"puentes eliminados: {n}");
                        break;
                    }

                case "set":
                    {
                        if (idiomaArg == null) { W("falta el idioma"); break; }
                        var (ok, msg) = LanguageSystem.SetGameLanguage(canon);
                        W(ok ? $"idioma del juego -> {msg}" : $"error: {msg}");
                        break;
                    }

                default:
                    W("uso: -bridge [estado|crear|quitar|set] [idioma]");
                    break;
            }
        }
        catch (Exception ex) { W("ERROR: " + ex.Message); }

        var texto = sb.ToString();
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "bridge.txt"), texto, Encoding.UTF8); } catch { }
        try { Console.OutputEncoding = Encoding.UTF8; Console.Write(texto); } catch { }
        return 0;
    }
}
