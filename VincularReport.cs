using System.Text;

namespace LodeonAdmin;

/// <summary>
/// Busca el id del Workshop de los mods instalados a mano, sin clave de API.
///
/// Los mods descargados por Steam traen About\PublishedFileId.txt; los copiados a
/// mano no, y sin ese archivo Lodeon no puede comprobarles actualizaciones. Esto
/// busca cada uno por su titulo en la pagina publica del taller y propone el id.
///
/// Solo propone coincidencias EXACTAS: escribir un id equivocado haria que una
/// actualizacion futura sustituyera el mod por otro distinto.
/// </summary>
public static class VincularReport
{
    public static int Run(string[] args)
    {
        bool aplicar = args.Any(a => a.Equals("-aplicar", StringComparison.OrdinalIgnoreCase));
        var sb = new StringBuilder();
        sb.AppendLine("=== VINCULAR MODS AL WORKSHOP ===");
        sb.AppendLine();
        sb.AppendLine("Metodo: busqueda en la pagina publica del taller (sin clave de API).");
        sb.AppendLine($"Aplicar cambios: {(aplicar ? "SI" : "no (solo propuesta)")}");
        sb.AppendLine();

        var propuestas = new List<Workshop.Vinculo>();
        var rechazos = new List<Workshop.Rechazo>();
        var lineas = new List<string>();

        var vinculados = Workshop.BuscarVinculos(l => lineas.Add(l), rechazos);

        sb.AppendLine("--- Progreso ---");
        foreach (var l in lineas) sb.AppendLine("  " + l);
        sb.AppendLine();

        if (vinculados.Count == 0)
        {
            sb.AppendLine("Ningun mod sin id coincide exactamente con un titulo del taller.");
            sb.AppendLine("Es normal en mods que no estan en Steam (los [ES], los de LoversLab).");
            sb.AppendLine();
            ResumenRechazos(sb, rechazos);
            sb.AppendLine("RESULTADO: OK (0 vinculos)");
            return Escribir(sb, true);
        }

        sb.AppendLine($"--- Coincidencias exactas: {vinculados.Count} ---");
        foreach (var v in vinculados)
        {
            sb.AppendLine($"  {v.Carpeta}");
            sb.AppendLine($"      titulo local : {v.NombreMod}");
            sb.AppendLine($"      titulo taller: {v.TituloWeb}");
            sb.AppendLine($"      id           : {v.Id}   (puntaje {v.Puntaje})");
        }
        sb.AppendLine();

        if (aplicar)
        {
            int ok = 0, fallos = 0;
            foreach (var v in vinculados)
            {
                if (Workshop.EscribirPublishedFileId(v.Carpeta, v.Id, out var err)) ok++;
                else { fallos++; sb.AppendLine($"  FALLO {v.Carpeta}: {err}"); }
            }
            sb.AppendLine($"Escritos: {ok}   Fallos: {fallos}");
            sb.AppendLine();
            sb.AppendLine(fallos == 0 ? "RESULTADO: OK" : "RESULTADO: hubo fallos");
            return Escribir(sb, fallos == 0);
        }

        sb.AppendLine("Nada escrito. Ejecuta con -aplicar para guardar los ids.");
        sb.AppendLine("RESULTADO: OK");
        return Escribir(sb, true);
    }

    private static void ResumenRechazos(StringBuilder sb, List<Workshop.Rechazo> rechazos)
    {
        if (rechazos.Count == 0) return;

        sb.AppendLine($"--- Descartados: {rechazos.Count} ---");
        foreach (var grupo in rechazos.GroupBy(r => r.Motivo).OrderByDescending(g => g.Count()))
        {
            sb.AppendLine($"  {grupo.Count()} x {grupo.Key}");
            foreach (var r in grupo.Take(4))
                sb.AppendLine($"      {r.Carpeta}");
            if (grupo.Count() > 4) sb.AppendLine($"      ... y {grupo.Count() - 4} mas");
        }
        sb.AppendLine();
    }

    private static int Escribir(StringBuilder sb, bool ok)
    {
        var txt = sb.ToString();
        try { File.WriteAllText(Path.Combine(AppConfig.ExeDir, "vincular.txt"), txt, Encoding.UTF8); } catch { }
        try { Console.OutputEncoding = Encoding.UTF8; Console.Write(txt); } catch { }
        return ok ? 0 : 1;
    }
}
