using System.Text;

namespace LodeonAdmin;

/// <summary>Informe de salud por consola: permite verificar sin abrir la ventana.</summary>
public static class HealthReport
{
    public static int Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== INFORME DE SALUD ===");
        sb.AppendLine();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var inf = Health.Generar();
        sw.Stop();

        sb.AppendLine($"Tiempo: {sw.ElapsedMilliseconds} ms");
        sb.AppendLine($"Errores en el registro: {inf.ErroresLog}   Avisos: {inf.AvisosLog}");
        sb.AppendLine();

        sb.AppendLine($"--- Mods mencionados en errores/avisos: {inf.Culpables.Count} ---");
        foreach (var c in inf.Culpables.Take(10))
        {
            sb.AppendLine($"  {c.Entradas,4} entradas {c.Marcos,6} marcos  {c.Mod}");
            if (c.Ejemplo.Length > 0) sb.AppendLine($"       {c.Ejemplo}");
        }
        sb.AppendLine();

        sb.AppendLine($"--- Sin soporte declarado de 1.7: {inf.SinSoporte17.Count} ---");
        foreach (var s in inf.SinSoporte17.Take(10)) sb.AppendLine("  " + s);
        sb.AppendLine();

        sb.AppendLine($"--- Incompatibilidades: {inf.Incompatibles.Count} ---");
        foreach (var s in inf.Incompatibles.Take(10)) sb.AppendLine("  " + s);
        sb.AppendLine();

        sb.AppendLine($"--- Dependencias rotas: {inf.DepsRotas.Count} ---");
        foreach (var s in inf.DepsRotas.Take(10)) sb.AppendLine("  " + s);
        sb.AppendLine();

        sb.AppendLine($"--- Reglas de orden incumplidas: {inf.OrdenRoto.Count} ---");
        foreach (var s in inf.OrdenRoto.Take(5)) sb.AppendLine("  " + s);
        sb.AppendLine();

        sb.AppendLine(inf.Sano ? "RESULTADO: SANO" : $"RESULTADO: {inf.Total} problema(s)");

        var txt = sb.ToString();
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "health.txt"), txt, Encoding.UTF8); } catch { }
        try { Console.OutputEncoding = Encoding.UTF8; Console.Write(txt); } catch { }
        return 0;
    }
}
