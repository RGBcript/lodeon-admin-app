using System.Text;

namespace LodeonAdmin;

/// <summary>Muestra que encuentra la deteccion automatica, sin abrir la ventana.</summary>
public static class DetectReport
{
    public static int Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== DETECCION ===");
        sb.AppendLine();

        var r = GameFinder.Detectar();
        sb.AppendLine("--- Encontrado ---");
        sb.AppendLine(r.Explicacion);
        sb.AppendLine();

        sb.AppendLine("--- Bibliotecas de Steam ---");
        var raices = GameFinder.RaicesSteam();
        if (raices.Count == 0) sb.AppendLine("  (ninguna)");
        foreach (var raiz in raices)
        {
            sb.AppendLine("  " + raiz);
            var libs = GameFinder.Bibliotecas(raiz);
            foreach (var l in libs) sb.AppendLine("    biblioteca: " + l);
        }
        sb.AppendLine();

        sb.AppendLine("--- Configuracion en uso ---");
        var cfg = Rim.Cfg;
        sb.AppendLine($"  GamePath   : {cfg.GamePath}");
        sb.AppendLine($"  valido     : {GameFinder.EsJuego(cfg.GamePath)}");
        sb.AppendLine($"  SteamCmd   : {cfg.SteamCmd}");
        sb.AppendLine($"  existe     : {File.Exists(cfg.SteamCmd)}");
        sb.AppendLine($"  Versiones  : {cfg.VersionsFolder}");
        sb.AppendLine($"  manual     : {cfg.RutasManuales}");
        sb.AppendLine($"  cargado de : {AppConfig.LoadedFrom}");
        if (AppConfig.RutasCambiadas.Length > 0)
            sb.AppendLine("  CAMBIO     : " + AppConfig.RutasCambiadas.Replace("\n", " "));
        sb.AppendLine();

        bool ok = GameFinder.EsJuego(cfg.GamePath);
        sb.AppendLine(ok ? "RESULTADO: OK" : "RESULTADO: juego no localizado");

        var txt = sb.ToString();
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "detect.txt"), txt, Encoding.UTF8); } catch { }
        try { Console.OutputEncoding = Encoding.UTF8; Console.Write(txt); } catch { }
        return ok ? 0 : 1;
    }
}
