using System.Text.Json;

namespace LodeonAdmin;

/// <summary>
/// Indice de posicion de cada mod, para que desactivar y reactivar NO cambie el
/// orden de carga.
///
/// Sin esto, al reactivar un mod se añade al final de ModsConfig.xml y en RimWorld
/// el orden importa: un parche que debe cargar despues de su mod original deja de
/// aplicarse, y el sintoma es un fallo silencioso.
///
/// El indice guardado es la posicion ABSOLUTA en la lista completa (activados y
/// desactivados a la vez), igual que hacia el lanzador original en PowerShell.
/// </summary>
public static class Order
{
    /// <summary>Carpeta de estado, junto al ejecutable: state/mod-order.json</summary>
    public static string Dir => Path.Combine(AppConfig.ExeDir, "state");

    public static string FilePath => Path.Combine(Dir, "mod-order.json");

    public static string Error { get; private set; } = "";

    /// <summary>Lee el indice. Claves en minusculas. Vacio si no existe o esta roto.</summary>
    public static Dictionary<string, int> Leer()
    {
        var res = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Error = "";
        try
        {
            if (!File.Exists(FilePath)) return res;
            var txt = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(txt)) return res;

            using var doc = JsonDocument.Parse(txt);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return res;

            foreach (var p in doc.RootElement.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var i))
                    res[p.Name] = i;
            }
        }
        catch (Exception ex) { Error = ex.Message; }
        return res;
    }

    /// <summary>Reescribe el indice entero con la lista dada.</summary>
    public static bool Guardar(IEnumerable<string> ids)
    {
        var h = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        foreach (var id in ids)
            if (!string.IsNullOrWhiteSpace(id) && !h.ContainsKey(id)) h[id] = i++;
        return Escribir(h);
    }

    /// <summary>
    /// Actualiza SOLO los mods presentes y conserva el indice de los ausentes.
    ///
    /// Imprescindible: si se sobreescribe entero al desactivar un mod, se pierde su
    /// posicion original y al reactivarlo cambia el orden de carga.
    /// </summary>
    public static bool Mezclar(IEnumerable<string> ids)
    {
        var h = Leer();

        // reindexar de forma compacta: los presentes toman su nuevo indice, y los
        // ausentes conservan el suyo reajustado para no solaparse
        var presentes = ids.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        var nuevo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // posicion relativa de cada presente segun su indice anterior
        var ordenPrevio = h;
        int siguiente = 0;
        foreach (var id in presentes)
        {
            int pos = ordenPrevio.TryGetValue(id, out var p) ? p : int.MaxValue;
            nuevo[id] = pos == int.MaxValue ? int.MaxValue - siguiente : pos;
        }

        // compactar a 0..n-1 conservando el orden relativo, y dejar los ausentes
        // conservados al final con su indice previo traducido
        var final = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int k = 0;
        foreach (var kv in nuevo.OrderBy(x => x.Value).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            final[kv.Key] = k++;

        // los que ya no estan activos conservan un indice por encima, para que al
        // reactivarlos vuelvan cerca de donde estaban
        foreach (var kv in h.OrderBy(x => x.Value))
        {
            if (final.ContainsKey(kv.Key)) continue;
            final[kv.Key] = k++;
        }

        return Escribir(final);
    }

    private static bool Escribir(Dictionary<string, int> h)
    {
        Error = "";
        try
        {
            if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
            // ordenado por indice para que el archivo sea legible
            var ordenado = h.OrderBy(x => x.Value)
                            .ToDictionary(x => x.Key, x => x.Value);
            var json = JsonSerializer.Serialize(ordenado,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
            return true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            return false;
        }
    }

    /// <summary>Indice guardado de un mod, o int.MaxValue si no hay dato.</summary>
    public static int Rango(Dictionary<string, int> orden, string packageId)
        => orden.TryGetValue(packageId, out var i) ? i : int.MaxValue;

    /// <summary>
    /// Devuelve la lista activa con los mods reinsertandos en su posicion original
    /// en lugar de al final. Los que no tienen indice guardado van al final.
    /// </summary>
    public static List<string> Reinsertar(List<string> activos, IEnumerable<string> reactivar)
    {
        var orden = Leer();
        var res = new List<string>(activos);

        foreach (var pk in reactivar.OrderBy(x => Rango(orden, x)))
        {
            int objetivo = Rango(orden, pk);

            int idx = res.Count;
            for (int i = 0; i < res.Count; i++)
            {
                if (Rango(orden, res[i]) > objetivo) { idx = i; break; }
            }
            res.Insert(idx, pk);
        }
        return res;
    }

    /// <summary>Cuántos mods tienen indice guardado.</summary>
    public static int Contar() => Leer().Count;
}
