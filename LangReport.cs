using System.Text;

namespace LodeonAdmin;

/// <summary>Diagnostico del sistema de idioma, para probar sin interfaz.</summary>
public static class LangReport
{
    public static int Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== SISTEMA DE IDIOMA ===");
        sb.AppendLine();

        var (detectado, detalle) = LanguageSystem.DetectUserLanguage();
        var (juego, juegoFolder) = LanguageSystem.GameLanguage();

        sb.AppendLine($"Idioma del usuario : {detectado}   ({detalle})");
        sb.AppendLine($"Idioma del juego   : {juego}   (\"{juegoFolder}\")");
        sb.AppendLine($"Coinciden          : {(detectado == juego ? "SI" : "NO")}");
        sb.AppendLine();

        var inv = LanguageSystem.Inventory(out var mods);
        sb.AppendLine($"Mods con carpetas de idioma: {mods.Count}");
        sb.AppendLine($"Idiomas distintos          : {inv.Count}");
        sb.AppendLine();

        sb.AppendLine("Top idiomas disponibles (num. de mods):");
        foreach (var kv in inv.OrderByDescending(x => x.Value).Take(12))
            sb.AppendLine($"  {kv.Value,4}  {kv.Key,-14} -> carpeta \"{LanguageSystem.PreferredFolderName(kv.Key)}\"");
        sb.AppendLine();

        // --- el diagnostico importante
        var deseado = LanguageSystem.PreferredFolderName(detectado);
        var puentes = LanguageSystem.BridgeCandidates(detectado);
        sb.AppendLine($"Para el idioma detectado ({detectado}) el juego espera: \"{deseado}\"");
        sb.AppendLine($"Mods activos que podrian usar su traduccion con un puente: {puentes.Count}");
        sb.AppendLine();
        foreach (var p in puentes.Take(25))
            sb.AppendLine($"  {p.ModName,-50} tiene \"{p.FolderName}\"");
        if (puentes.Count > 25) sb.AppendLine($"  ... y {puentes.Count - 25} mas");
        sb.AppendLine();

        // --- simulacion de cobertura
        int conExacta = 0, conVariante = 0, soloIngles = 0, sinNada = 0;
        var plan = LanguageSystem.Plan(detectado);
        foreach (var c in plan)
        {
            if (c.Reason == "exacta") conExacta++;
            else if (c.Reason == "variante") conVariante++;
            else if (c.Reason == "respaldo ingles") soloIngles++;
            else sinNada++;
        }
        sb.AppendLine($"COBERTURA para {detectado} (mods activos: {plan.Count}):");
        sb.AppendLine($"  ya tienen la carpeta exacta : {conExacta}");
        sb.AppendLine($"  tienen variante (puente)    : {conVariante}");
        sb.AppendLine($"  solo ingles                 : {soloIngles}");
        sb.AppendLine($"  sin nada                    : {sinNada}");
        sb.AppendLine();

        var texto = sb.ToString();
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "langreport.txt"), texto, Encoding.UTF8); } catch { }
        try { Console.OutputEncoding = Encoding.UTF8; Console.Write(texto); } catch { }
        return 0;
    }
}
