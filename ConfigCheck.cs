using System.Text;

namespace LodeonAdmin;

/// <summary>Informa del estado de ModsConfig.xml y de sus copias.</summary>
public static class ConfigCheck
{
    public static int Run()
    {
        var sb = new StringBuilder();
        var (estado, detalle, n) = Rim.EstadoModsConfig();

        sb.AppendLine("=== MODS CONFIG ===");
        sb.AppendLine($"Archivo   : {Rim.ModsConfigPath}");
        sb.AppendLine($"Estado    : {estado}");
        if (detalle.Length > 0) sb.AppendLine($"Detalle   : {detalle}");
        sb.AppendLine($"Mods      : {n}");

        var copias = Rim.CopiasModsConfig();
        sb.AppendLine($"Copias    : {copias.Count}");
        foreach (var c in copias.Take(5))
            sb.AppendLine($"  {Path.GetFileName(c)}  {File.GetLastWriteTime(c):yyyy-MM-dd HH:mm}");

        sb.AppendLine();
        sb.AppendLine(estado == Rim.EstadoConfig.Ok ? "RESULTADO: OK" : $"RESULTADO: {estado}");
        var txt = sb.ToString();
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "configcheck.txt"), txt, Encoding.UTF8); } catch { }
        try { Console.OutputEncoding = Encoding.UTF8; Console.Write(txt); } catch { }
        return estado == Rim.EstadoConfig.Ok ? 0 : 1;
    }
}