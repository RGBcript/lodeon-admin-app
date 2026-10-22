using System.Text;
using System.Xml.Linq;

namespace LodeonAdmin;

/// <summary>
/// Ordenador de carga de mods.
///
/// RimWorld carga los mods en el orden de la lista activa. El orden importa: un parche
/// debe ir despues del mod que parchea, un framework antes de quien lo usa.
/// Los mods declaran sus restricciones en About.xml:
///   loadAfter  -> debe ir DESPUES de esos packageId
///   loadBefore -> debe ir ANTES de esos packageId
///
/// Estrategia (inspirada en como lo resuelve RimSort, simplificada):
///   1. Separar en niveles (tiers): 0 nucleo/oficial, 1 frameworks, 2 resto, 3 fondo.
///   2. Dentro de cada nivel, ordenar el grafo con Kahn (topologico).
///   3. Si queda un ciclo, romperlo de forma determinista y reportarlo.
///   4. Nunca perder mods: todo mod de la lista aparece exactamente una vez.
/// </summary>
public static class Sorter
{
    public class Nodo
    {
        public string PackageId = "";
        public string Name = "";
        public List<string> After = new();      // debe ir despues de
        public List<string> Before = new();     // debe ir antes de
        public int Tier;
    }

    public class Resultado
    {
        public List<string> Orden = new();             // packageIds ordenados
        public List<string> Avisos = new();            // problemas detectados
        public int Ciclos;
        public bool Ok => Ciclos == 0 && Avisos.Count == 0;
    }

    /// <summary>Lee las reglas de orden de todos los mods instalados.</summary>
    public static Dictionary<string, Nodo> LeerGrafo(out int externas)
    {
        externas = 0;
        var mapa = new Dictionary<string, Nodo>(StringComparer.OrdinalIgnoreCase);
        var dir = Rim.ModsDir;
        if (!Directory.Exists(dir)) return mapa;

        foreach (var d in Directory.GetDirectories(dir))
        {
            if (Path.GetFileName(d).StartsWith("_")) continue;
            var about = Path.Combine(d, "About", "About.xml");
            if (!File.Exists(about)) continue;

            try
            {
                var doc = XDocument.Load(about);
                var r = doc.Root;
                if (r == null) continue;
                var pk = (r.Element("packageId")?.Value ?? "").Trim();
                if (pk.Length == 0) continue;

                var n = new Nodo
                {
                    PackageId = pk,
                    Name = (r.Element("name")?.Value ?? pk).Trim(),
                };

                foreach (var li in r.Element("loadAfter")?.Elements("li") ?? Enumerable.Empty<XElement>())
                {
                    var v = li.Value.Trim();
                    if (v.Length > 0) n.After.Add(v);
                }
                foreach (var li in r.Element("loadBefore")?.Elements("li") ?? Enumerable.Empty<XElement>())
                {
                    var v = li.Value.Trim();
                    if (v.Length > 0) n.Before.Add(v);
                }

                mapa[pk] = n;
            }
            catch { }
        }

        // contar referencias a mods que no estan instalados
        foreach (var n in mapa.Values)
            foreach (var r in n.After.Concat(n.Before))
                if (!mapa.ContainsKey(r)) externas++;

        return mapa;
    }

    /// <summary>
    /// Asigna nivel a cada mod. Los niveles son solo un DESEMPATE: nunca deben
    /// romper una regla de orden. Un framework con loadAfter hacia un mod del
    /// nivel 2 tiene que seguir respetandolo.
    /// </summary>
    public static void AsignarTiers(Dictionary<string, Nodo> mapa)
    {
        // por defecto, nivel 2
        foreach (var n in mapa.Values) n.Tier = 2;

        // Nivel 0: nucleo y oficiales
        foreach (var n in mapa.Values)
        {
            var id = n.PackageId.ToLowerInvariant();
            if (id.StartsWith("ludeon.") || id.Contains("harmony") || id.Contains("prepatcher"))
                n.Tier = 0;
        }

        // Nivel 1: frameworks conocidos (solo desempate, no regla)
        string[] frameworks =
        {
            "framework", "xml extensions", "universum", "jecstools", "hugslib"
        };
        foreach (var n in mapa.Values)
        {
            if (n.Tier == 0) continue;
            var nombre = n.Name.ToLowerInvariant();
            if (frameworks.Any(f => nombre.Contains(f))) n.Tier = 1;
        }
    }

    /// <summary>Ordena una lista de packageId segun las reglas leidas.</summary>
    public static Resultado Ordenar(IEnumerable<string> activos, Dictionary<string, Nodo>? grafo = null)
    {
        var res = new Resultado();
        var lista = activos.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
        var mapa = grafo ?? LeerGrafo(out _);

        AsignarTiers(mapa);

        // nodos: solo los de la lista activa, pero conservando sus reglas
        var nodos = new Dictionary<string, Nodo>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in lista)
        {
            if (nodos.ContainsKey(id)) continue;
            if (mapa.TryGetValue(id, out var n)) nodos[id] = n;
            else nodos[id] = new Nodo { PackageId = id, Name = id, Tier = 2 };
        }

        // aristas: u -> v significa "u debe ir antes que v"
        var aristas = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var grado = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in nodos.Keys)
        {
            aristas[id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            grado[id] = 0;
        }

        void Arista(string a, string b)
        {
            if (!nodos.ContainsKey(a) || !nodos.ContainsKey(b)) return;
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return;
            if (aristas[a].Add(b)) grado[b]++;
        }

        foreach (var id in nodos.Keys)
        {
            var n = nodos[id];
            foreach (var dep in n.After) Arista(dep, id);     // la dependencia va antes
            foreach (var dep in n.Before) Arista(id, dep);    // este va antes
        }

        // ordenar por tier y nombre para que el resultado sea determinista
        int OrdenTier(string id) => nodos[id].Tier;
        string Nombre(string id) => nodos[id].Name.ToLowerInvariant();

        // UN SOLO grafo: el tier solo se usa como desempate entre mods sin relacion.
        // Separar por tiers rompia las reglas cruzadas (un framework que depende de
        // un mod del nivel 2 quedaba antes de el).
        var pendientes = new HashSet<string>(nodos.Keys, StringComparer.OrdinalIgnoreCase);
        var gradoLocal = pendientes.ToDictionary(k => k, k => 0, StringComparer.OrdinalIgnoreCase);
        foreach (var u in pendientes)
            foreach (var v in aristas[u])
                if (pendientes.Contains(v)) gradoLocal[v]++;

        var libres = pendientes.Where(k => gradoLocal[k] == 0)
                               .OrderBy(OrdenTier).ThenBy(Nombre).ToList();

        var salida = new List<string>();
        var yaPuestos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (libres.Count > 0)
        {
            var n = libres[0];
            libres.RemoveAt(0);
            if (!pendientes.Remove(n)) continue;
            salida.Add(n);
            yaPuestos.Add(n);

            foreach (var v in aristas[n])
            {
                if (!pendientes.Contains(v)) continue;
                gradoLocal[v]--;
                if (gradoLocal[v] == 0)
                {
                    libres.Add(v);
                    libres = libres.OrderBy(OrdenTier).ThenBy(Nombre).ToList();
                }
            }
        }

        // ciclo: romperlo por orden alfabetico y avisar
        if (pendientes.Count > 0)
        {
            res.Ciclos += pendientes.Count;
            res.Avisos.Add($"{pendientes.Count} mods forman un ciclo de orden y se colocan al final: " +
                           string.Join(", ", pendientes.Take(5)));
            foreach (var p in pendientes.OrderBy(Nombre)) { salida.Add(p); yaPuestos.Add(p); }
        }

        // seguridad: ningun mod perdido
        var sobrantes = nodos.Keys.Where(k => !yaPuestos.Contains(k)).ToList();
        if (sobrantes.Count > 0)
        {
            res.Avisos.Add($"{sobrantes.Count} mods no se pudieron ordenar y se añaden al final.");
            foreach (var s in sobrantes.OrderBy(Nombre)) { salida.Add(s); yaPuestos.Add(s); }
        }

        res.Orden = salida;

        // detectar reglas incumplidas (solo informativo)
        var pos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < salida.Count; i++) pos[salida[i]] = i;
        foreach (var id in salida)
        {
            if (!nodos.TryGetValue(id, out var n)) continue;
            foreach (var dep in n.After)
            {
                if (!pos.ContainsKey(dep)) continue;
                if (pos[dep] > pos[id])
                    res.Avisos.Add($"{n.Name} debería ir después de {dep} y quedó antes.");
            }
        }

        return res;
    }

    /// <summary>Incompatibilidades declaradas por los mods activos.</summary>
    public static List<string> Incompatibilidades(IEnumerable<string> activos)
    {
        var res = new List<string>();
        var set = new HashSet<string>(activos.Select(x => x.Trim()), StringComparer.OrdinalIgnoreCase);
        var dir = Rim.ModsDir;
        if (!Directory.Exists(dir)) return res;

        foreach (var d in Directory.GetDirectories(dir))
        {
            if (Path.GetFileName(d).StartsWith("_")) continue;
            var about = Path.Combine(d, "About", "About.xml");
            if (!File.Exists(about)) continue;
            try
            {
                var doc = XDocument.Load(about);
                var r = doc.Root;
                if (r == null) continue;
                var pk = (r.Element("packageId")?.Value ?? "").Trim();
                if (pk.Length == 0 || !set.Contains(pk)) continue;
                var nombre = (r.Element("name")?.Value ?? pk).Trim();

                foreach (var li in r.Element("incompatibleWith")?.Elements("li") ?? Enumerable.Empty<XElement>())
                {
                    var otro = li.Value.Trim();
                    if (otro.Length == 0) continue;
                    if (set.Contains(otro))
                        res.Add($"{nombre}  es incompatible con  {otro}");
                }
            }
            catch { }
        }
        return res;
    }
}
