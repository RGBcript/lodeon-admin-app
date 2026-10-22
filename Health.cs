using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LodeonAdmin;

/// <summary>
/// Diagnóstico de salud de la instalación.
///
/// La parte útil es atribuir los errores del registro a un mod concreto. RimWorld no
/// escribe el nombre del mod en la línea de error, pero las trazas incluyen marcos
/// del tipo `at Namespace.Clase.Metodo(...)`, y ese primer segmento coincide casi
/// siempre con el nombre del ensamblado, un segmento del packageId o el nombre de la
/// carpeta. Con esas tres vías se atribuye el 99% de los marcos.
/// </summary>
public static class Health
{
    public class Culpable
    {
        public string Mod = "";
        public string Carpeta = "";
        public int Entradas;      // entradas del registro donde aparece
        public int Marcos;        // marcos de traza
        public string Ejemplo = "";
    }

    public class Informe
    {
        public int ErroresLog;
        public int AvisosLog;
        public List<Culpable> Culpables = new();
        public List<string> Incompatibles = new();
        public List<string> SinSoporte17 = new();
        public List<string> DepsRotas = new();
        public List<string> OrdenRoto = new();

        /// <summary>SinSoporte17 queda fuera: en este pack 262 mods declaran solo
        /// hasta 1.6 y el juego los acepta, asi que no distingue nada.</summary>
        public int Total => Incompatibles.Count + DepsRotas.Count + OrdenRoto.Count;

        public bool Sano => ErroresLog == 0 && Total == 0;
    }

    // raíces que pertenecen al motor, no a un mod
    static readonly HashSet<string> Vanilla = new(StringComparer.OrdinalIgnoreCase)
    {
        "Verse", "System", "UnityEngine", "RimWorld", "Brutal", "LunarFramework",
        "MonoMod", "Microsoft", "Newtonsoft", "HarmonyLib", "JetBrains", "TMPro",
        "Steamworks", "LudeonTK", "Waylith", "RimWorldWaylith", "Cysharp", "NAudio",
        "NVorbis", "Unity", "SystemCore", "Iced", "Facepunch", "Krafs", "dotnetzip",
        "UnityStubs", "Logger", "Logging", "Sentry", "BepInEx",
    };

    class Mapas
    {
        public Dictionary<string, string> Asm = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Pk = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Nombre = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Construye los tres índices de atribución a partir de los mods instalados.</summary>
    static Mapas ConstruirMapas()
    {
        var m = new Mapas();
        var dir = Rim.ModsDir;
        if (!Directory.Exists(dir)) return m;

        foreach (var d in Directory.GetDirectories(dir))
        {
            var carpeta = Path.GetFileName(d);
            if (carpeta.StartsWith("_")) continue;

            // packageId
            var about = Path.Combine(d, "About", "About.xml");
            if (File.Exists(about))
            {
                try
                {
                    var doc = XDocument.Load(about);
                    var pk = (doc.Root?.Element("packageId")?.Value ?? "").Trim();
                    foreach (var seg in pk.Split('.', StringSplitOptions.RemoveEmptyEntries))
                        if (seg.Length >= 3 && !m.Pk.ContainsKey(seg)) m.Pk[seg] = carpeta;
                }
                catch { }
            }

            // nombre normalizado (sin simbolos ni espacios)
            var norm = new string(carpeta.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            if (norm.Length >= 3 && !m.Nombre.ContainsKey(norm)) m.Nombre[norm] = carpeta;

            // ensamblados
            foreach (var sub in new[] { "Assemblies", "1.7\\Assemblies", "1.6\\Assemblies" })
            {
                var p = Path.Combine(d, sub);
                if (!Directory.Exists(p)) continue;
                try
                {
                    foreach (var f in Directory.GetFiles(p, "*.dll"))
                    {
                        var n = Path.GetFileNameWithoutExtension(f);
                        if (!m.Asm.ContainsKey(n)) m.Asm[n] = carpeta;
                    }
                }
                catch { }
            }
        }
        return m;
    }

    /// <summary>Intenta atribuir una raíz de namespace a una carpeta de mod.</summary>
    static string? Atribuir(string raiz, Mapas m)
    {
        if (Vanilla.Contains(raiz)) return null;
        if (m.Asm.TryGetValue(raiz, out var a)) return a;
        if (m.Pk.TryGetValue(raiz, out var p)) return p;

        var r = raiz.ToLowerInvariant();
        if (r.Length < 3) return null;
        foreach (var kv in m.Nombre)
            if (kv.Key.Contains(r) || r.Contains(kv.Key)) return kv.Value;
        return null;
    }

    static readonly Regex Nivel = new(@"^\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}[.,]\d+\s+(ERROR|WARN|INFO|MESSAGE|DEBUG|FATAL)\b",
                                       RegexOptions.Compiled);
    static readonly Regex Marco = new(@"at\s+([A-Za-z_][A-Za-z0-9_]*)\.[A-Za-z0-9_<>`\+\.]*\s*\(",
                                       RegexOptions.Compiled);

    /// <summary>
    /// Lee el registro y devuelve los mods que aparecen en entradas de error o aviso.
    /// Cuenta ENTRADAS (una excepción = una), no marcos, para que un mod con traza
    /// larga no aplaste al resto.
    /// </summary>
    public static (List<Culpable> culpables, int errores, int avisos) AnalizarLog()
    {
        var mapa = ConstruirMapas();
        var acc = new Dictionary<string, Culpable>(StringComparer.OrdinalIgnoreCase);
        int errores = 0, avisos = 0;

        var texto = Rim.ReadLog();
        if (texto.Length == 0) return (new List<Culpable>(), 0, 0);

        var lineas = texto.Split('\n');
        string nivelActual = "";
        string mensaje = "";
        var modsEntrada = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var marcosEntrada = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string? primerMod = null;

        void CerrarEntrada()
        {
            if (modsEntrada.Count > 0 && (nivelActual == "ERROR" || nivelActual == "WARN" || nivelActual == "FATAL"))
            {
                // Solo el PRIMER mod de la traza se lleva la entrada: es el que estaba
                // mas cerca del punto donde fallo. Los demas solo suman marcos, porque
                // suelen ser envoltorios (cargadores, parches, otros loggers).
                foreach (var mod in modsEntrada)
                {
                    if (!acc.TryGetValue(mod, out var c))
                        acc[mod] = c = new Culpable { Mod = mod, Carpeta = mod };
                    c.Marcos += marcosEntrada.TryGetValue(mod, out var n) ? n : 0;
                    if (string.Equals(mod, primerMod, StringComparison.OrdinalIgnoreCase))
                    {
                        c.Entradas++;
                        if (c.Ejemplo.Length == 0)
                            c.Ejemplo = mensaje.Length > 180 ? mensaje[..180] : mensaje;
                    }
                    else if (c.Ejemplo.Length == 0)
                    {
                        c.Ejemplo = mensaje.Length > 180 ? mensaje[..180] : mensaje;
                    }
                }
            }
            modsEntrada.Clear();
            marcosEntrada.Clear();
            primerMod = null;
            mensaje = "";
        }

        foreach (var cruda in lineas)
        {
            var l = cruda.TrimEnd('\r');
            var mNivel = Nivel.Match(l);
            if (mNivel.Success)
            {
                CerrarEntrada();
                nivelActual = mNivel.Groups[1].Value.ToUpperInvariant();
                if (nivelActual == "ERROR" || nivelActual == "FATAL") errores++;
                else if (nivelActual == "WARN") avisos++;
                mensaje = l.Substring(mNivel.Index + mNivel.Length).Trim();
                continue;
            }
            if (l.Trim().Length == 0) continue;

            // marcos de traza y cualquier mención de namespace
            foreach (Match fm in Marco.Matches(l))
            {
                // Los marcos del propio registrador no son el culpable: aparecen en
                // todas las entradas y ensuciarian la atribucion.
                if (l.Contains("Log", StringComparison.Ordinal) &&
                    (l.Contains("Logging", StringComparison.Ordinal) ||
                     l.Contains("Log.Warning", StringComparison.Ordinal) ||
                     l.Contains("Log.Error", StringComparison.Ordinal) ||
                     l.Contains("Log.Message", StringComparison.Ordinal)))
                    continue;

                var raiz = fm.Groups[1].Value;
                var mod = Atribuir(raiz, mapa);
                if (mod == null) continue;
                modsEntrada.Add(mod);
                marcosEntrada[mod] = (marcosEntrada.TryGetValue(mod, out var c2) ? c2 : 0) + 1;
                primerMod ??= mod;
            }

            // ruta explicita a la carpeta del mod
            if (l.Contains("Mods", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Match pm in Regex.Matches(l, @"Mods[\\/]([^\\/\r\n""]+)"))
                {
                    var carpeta = pm.Groups[1].Value.Trim();
                    if (carpeta.Length < 2) continue;
                    var dir = Path.Combine(Rim.ModsDir, carpeta);
                    if (Directory.Exists(dir)) modsEntrada.Add(carpeta);
                }
            }
        }
        CerrarEntrada();

        var lista = acc.Values.OrderByDescending(x => x.Entradas).ThenByDescending(x => x.Marcos).ToList();
        return (lista, errores, avisos);
    }

    /// <summary>Mods activos cuyo About.xml no declara soporte para 1.7.</summary>
    public static List<string> SinSoporte17()
    {
        var res = new List<string>();
        var dir = Rim.ModsDir;
        if (!Directory.Exists(dir)) return res;
        var activos = new HashSet<string>(Rim.ActiveMods(), StringComparer.OrdinalIgnoreCase);

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
                if (pk.Length == 0 || !activos.Contains(pk)) continue;

                var versiones = r.Element("supportedVersions")?.Elements("li")
                                 .Select(x => x.Value.Trim()).ToList() ?? new List<string>();
                if (versiones.Count == 0) continue;          // no declara nada: no se puede juzgar
                if (versiones.Any(v => v.StartsWith("1.7"))) continue;

                var nombre = (r.Element("name")?.Value ?? pk).Trim();
                res.Add($"{nombre}   [{string.Join(",", versiones)}]");
            }
            catch { }
        }
        return res.OrderBy(x => x).ToList();
    }

    /// <summary>Informe completo, sin red (las actualizaciones se consultan aparte).</summary>
    public static Informe Generar(bool incluirLog = true)
    {
        var inf = new Informe();
        var activos = Rim.ActiveMods();

        if (incluirLog)
        {
            var (culpables, errores, avisos) = AnalizarLog();
            inf.Culpables = culpables;
            inf.ErroresLog = errores;
            inf.AvisosLog = avisos;
        }

        inf.Incompatibles = Sorter.Incompatibilidades(activos);
        inf.SinSoporte17 = SinSoporte17();

        // dependencias rotas
        var mods = Rim.AllMods();
        var porId = new Dictionary<string, Rim.ModInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in mods) if (m.PackageId.Length > 0) porId[m.PackageId] = m;
        var act = new HashSet<string>(activos, StringComparer.OrdinalIgnoreCase);

        foreach (var m in mods.Where(x => x.Active && !x.SinAbout))
        {
            var about = Path.Combine(Rim.ModsDir, m.Folder, "About", "About.xml");
            if (!File.Exists(about)) continue;
            try
            {
                var doc = XDocument.Load(about);
                foreach (var li in doc.Root?.Element("modDependencies")?.Elements("li")
                                   ?? Enumerable.Empty<XElement>())
                {
                    var dep = (li.Element("packageId")?.Value ?? "").Trim();
                    if (dep.Length == 0 || dep.StartsWith("ludeon.", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!porId.ContainsKey(dep)) inf.DepsRotas.Add($"{m.Name}  necesita  {dep}  (no instalado)");
                    else if (!act.Contains(dep)) inf.DepsRotas.Add($"{m.Name}  necesita  {dep}  (desactivado)");
                }
            }
            catch { }
        }

        // reglas de orden incumplidas
        var r = Sorter.Ordenar(activos);
        inf.OrdenRoto = r.Avisos.Where(a => a.Contains("debería ir")).Take(10).ToList();

        return inf;
    }

    public static string Resumen(Informe inf)
    {
        var sb = new StringBuilder();
        if (inf.Sano)
        {
            sb.AppendLine("Sin problemas detectados.");
            sb.AppendLine();
            sb.AppendLine($"Registro: {inf.AvisosLog} aviso(s), 0 errores.");
            return sb.ToString();
        }

        if (inf.ErroresLog > 0) sb.AppendLine($"Errores en el registro: {inf.ErroresLog}");
        if (inf.Culpables.Count > 0) sb.AppendLine($"Mods mencionados en errores: {inf.Culpables.Count}");
        if (inf.Incompatibles.Count > 0) sb.AppendLine($"Incompatibilidades: {inf.Incompatibles.Count}");
        if (inf.DepsRotas.Count > 0) sb.AppendLine($"Dependencias rotas: {inf.DepsRotas.Count}");
        if (inf.OrdenRoto.Count > 0) sb.AppendLine($"Reglas de orden incumplidas: {inf.OrdenRoto.Count}");
        if (inf.SinSoporte17.Count > 0) sb.AppendLine($"Sin soporte declarado de 1.7: {inf.SinSoporte17.Count}");
        return sb.ToString();
    }
}
