using System.Text;

namespace LodeonAdmin;

/// <summary>Prueba del ordenador sin abrir la interfaz.</summary>
public static class SortReport
{
    public static int Run()
    {
        var sb = new StringBuilder();
        int fallos = 0;

        var activos = Rim.ActiveMods();
        sb.AppendLine($"Mods activos: {activos.Count}");

        var grafo = Sorter.LeerGrafo(out var externas);
        sb.AppendLine($"Mods en el grafo: {grafo.Count}");
        sb.AppendLine($"Referencias a mods no instalados: {externas}");
        sb.AppendLine();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = Sorter.Ordenar(activos, grafo);
        sw.Stop();

        sb.AppendLine($"Tiempo: {sw.ElapsedMilliseconds} ms");
        sb.AppendLine($"Ordenados: {r.Orden.Count}");
        sb.AppendLine($"Ciclos: {r.Ciclos}");
        sb.AppendLine($"Avisos: {r.Avisos.Count}");
        foreach (var a in r.Avisos.Take(12)) sb.AppendLine("  · " + a);
        sb.AppendLine();

        // --- comprobaciones duras
        void Check(string nombre, bool ok, string detalle)
        {
            sb.AppendLine($"  {(ok ? "OK   " : "FALLO")}  {nombre,-42} {detalle}");
            if (!ok) fallos++;
        }

        Check("no se pierde ningun mod", r.Orden.Count == activos.Count,
              $"{r.Orden.Count} vs {activos.Count}");
        Check("no hay duplicados",
              r.Orden.Distinct(StringComparer.OrdinalIgnoreCase).Count() == r.Orden.Count,
              $"{r.Orden.Distinct(StringComparer.OrdinalIgnoreCase).Count()} unicos");
        // comparar sin distinguir mayusculas: la lista activa y About.xml pueden
        // diferir en eso (p.ej. "Ludeon.Rimworld.Biotech" vs "ludeon.rimworld.biotech")
        var setA = new HashSet<string>(activos, StringComparer.OrdinalIgnoreCase);
        var setB = new HashSet<string>(r.Orden, StringComparer.OrdinalIgnoreCase);
        Check("mismos mods que la lista activa", setA.SetEquals(setB),
              $"{setA.Count} vs {setB.Count}");

        // --- reglas loadAfter respetadas
        int reglas = 0, cumplidas = 0;
        var pos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < r.Orden.Count; i++) pos[r.Orden[i]] = i;

        foreach (var id in r.Orden)
        {
            if (!grafo.TryGetValue(id, out var n)) continue;
            foreach (var dep in n.After)
            {
                if (!pos.ContainsKey(dep)) continue;
                reglas++;
                if (pos[dep] < pos[id]) cumplidas++;
            }
        }
        Check("reglas loadAfter respetadas", reglas == 0 || cumplidas == reglas,
              $"{cumplidas} de {reglas}");

        Check("ninguna regla incumplida reportada",
              r.Avisos.Count(x => x.Contains("debería ir después")) == 0,
              $"{r.Avisos.Count(x => x.Contains("debería ir después"))} incumplidas");

        // --- incompatibilidades
        var inc = Sorter.Incompatibilidades(activos);
        sb.AppendLine();
        sb.AppendLine($"Incompatibilidades entre mods activos: {inc.Count}");
        foreach (var i in inc.Take(10)) sb.AppendLine("  · " + i);

        // --- muestra del orden
        sb.AppendLine();
        sb.AppendLine("Primeros 8 del orden calculado:");
        foreach (var x in r.Orden.Take(8))
            sb.AppendLine($"  {x}  (tier {(grafo.TryGetValue(x, out var g) ? g.Tier : 2)})");
        sb.AppendLine("Ultimos 5:");
        foreach (var x in r.Orden.Skip(Math.Max(0, r.Orden.Count - 5)))
            sb.AppendLine($"  {x}");

        sb.AppendLine();
        sb.AppendLine(fallos == 0 ? "RESULTADO: OK" : $"RESULTADO: {fallos} FALLOS");

        var texto = sb.ToString();
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "sortreport.txt"), texto, Encoding.UTF8); } catch { }
        try { Console.OutputEncoding = Encoding.UTF8; Console.Write(texto); } catch { }
        return fallos == 0 ? 0 : 1;
    }
}
