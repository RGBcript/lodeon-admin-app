using System.Text;

namespace LodeonAdmin;

/// <summary>
/// Comprueba que reactivar un mod lo devuelve a su posicion original y no al final.
///
/// Simula el caso real: se desactivan tres mods del medio de la lista, se guarda el
/// indice, y se vuelven a activar. Si la posicion se restaura, el orden de carga
/// queda como estaba. Compara tambien con el comportamiento antiguo (añadir al
/// final) para que la diferencia sea visible.
/// </summary>
public static class OrderCheck
{
    public static int Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== ORDEN DE CARGA ===");
        sb.AppendLine();

        var activos = Rim.ActiveMods();
        sb.AppendLine($"Mods activos      : {activos.Count}");
        sb.AppendLine($"Archivo de indice : {Order.FilePath}");
        sb.AppendLine($"Existe            : {File.Exists(Order.FilePath)}");

        var orden = Order.Leer();
        sb.AppendLine($"Mods indexados    : {orden.Count}");
        if (Order.Error.Length > 0) sb.AppendLine($"Error de lectura  : {Order.Error}");
        sb.AppendLine();

        if (activos.Count < 10)
        {
            sb.AppendLine("Hacen falta al menos 10 mods activos para probar.");
            return Escribir(sb);
        }

        // semilla: si no hay indice, se crea con la lista actual
        if (orden.Count == 0)
        {
            Order.Guardar(activos);
            orden = Order.Leer();
            sb.AppendLine($"Indice creado desde la lista actual ({orden.Count} mods).");
            sb.AppendLine();
        }

        // elegir tres mods del medio, bien separados
        var victimas = new List<string>();
        for (int k = 1; k <= 3; k++)
        {
            int pos = activos.Count * k / 4;
            if (pos > 0 && pos < activos.Count) victimas.Add(activos[pos]);
        }
        victimas = victimas.Distinct().ToList();

        sb.AppendLine("--- Mods desactivados para la prueba ---");
        foreach (var v in victimas)
            sb.AppendLine($"  posicion {activos.IndexOf(v),3} : {v}");
        sb.AppendLine();

        // caso A: comportamiento antiguo (añadir al final)
        var antigua = activos.Where(x => !victimas.Contains(x)).ToList();
        antigua.AddRange(victimas);

        // caso B: comportamiento nuevo (reinsertar)
        var nueva = Order.Reinsertar(
            activos.Where(x => !victimas.Contains(x)).ToList(), victimas);

        sb.AppendLine("--- Resultado ---");
        int igualesA = 0, igualesB = 0;
        for (int i = 0; i < activos.Count && i < nueva.Count; i++)
        {
            if (antigua[i] == activos[i]) igualesA++;
            if (nueva[i] == activos[i]) igualesB++;
        }

        sb.AppendLine($"  Al final (antes) : {igualesA}/{activos.Count} posiciones intactas");
        sb.AppendLine($"  Reinsertar (ahora): {igualesB}/{activos.Count} posiciones intactas");
        sb.AppendLine();

        sb.AppendLine("--- Posiciones tras reactivar ---");
        foreach (var v in victimas)
            sb.AppendLine($"  {v}\n      estaba en {activos.IndexOf(v),3}   ->   ahora en {nueva.IndexOf(v),3}");
        sb.AppendLine();

        // la prueba pasa si cada victima vuelve exactamente a su sitio
        bool ok = victimas.All(v => activos.IndexOf(v) == nueva.IndexOf(v));

        // y no debe dejar huecos ni duplicados
        bool integridad = nueva.Count == activos.Count
                          && nueva.Distinct(StringComparer.OrdinalIgnoreCase).Count() == activos.Count;

        sb.AppendLine($"Posiciones restauradas : {(ok ? "SI" : "NO")}");
        sb.AppendLine($"Lista integra          : {(integridad ? "SI" : "NO")} ({nueva.Count} elementos, {nueva.Distinct(StringComparer.OrdinalIgnoreCase).Count()} unicos)");
        sb.AppendLine();
        sb.AppendLine(ok && integridad ? "RESULTADO: OK" : "RESULTADO: FALLO");

        return Escribir(sb, ok && integridad);
    }

    private static int Escribir(StringBuilder sb, bool ok = false)
    {
        var txt = sb.ToString();
        try { File.WriteAllText(Path.Combine(AppConfig.ExeDir, "ordercheck.txt"), txt, Encoding.UTF8); } catch { }
        try { Console.OutputEncoding = Encoding.UTF8; Console.Write(txt); } catch { }
        return ok ? 0 : 1;
    }
}
