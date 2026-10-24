using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LodeonAdmin;

/// <summary>
/// Descarga e instalacion desde el Workshop por SteamCMD, actualizacion de mods
/// instalados y perfiles de lista. Recupera lo que existia en la version PowerShell.
/// </summary>
public static class Workshop
{
    // ------------------------------------------------------------------ SteamCMD

    /// <summary>
    /// Descarga uno o varios items en Una sola invocacion de SteamCMD.
    ///
    /// SteamCMD acepta varios "+workshop_download_item" antes de "+quit" y reutiliza
    /// la sesion de login. Lanzar un proceso por mod arranca y para SteamCMD entero
    /// cada vez, que es lo que hacia la descarga lenta.
    ///
    /// <paramref name="avance"/> recibe el progreso por linea para poder mostrarlo.
    /// </summary>
    public static (List<string> ok, string log) Descargar(
        IEnumerable<string> ids, Action<string>? avance = null)
    {
        var lista = ids.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
        var ok = new List<string>();
        var log = new StringBuilder();

        if (lista.Count == 0) return (ok, "");
        if (!File.Exists(Rim.Cfg.SteamCmd))
        {
            log.AppendLine("No se encuentra SteamCMD en: " + Rim.Cfg.SteamCmd);
            log.AppendLine("Puedes indicar otra ruta en el apartado de configuración.");
            return (ok, log.ToString());
        }

        var psi = new ProcessStartInfo(Rim.Cfg.SteamCmd)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("+force_install_dir");
        psi.ArgumentList.Add(DirectorioTrabajo);
        psi.ArgumentList.Add("+login");
        psi.ArgumentList.Add("anonymous");
        foreach (var id in lista)
        {
            psi.ArgumentList.Add("+workshop_download_item");
            psi.ArgumentList.Add("294100");
            psi.ArgumentList.Add(id);
        }
        psi.ArgumentList.Add("+quit");

        try
        {
            using var p = Process.Start(psi);
            if (p == null)
            {
                log.AppendLine("No se pudo iniciar SteamCMD.");
                return (ok, log.ToString());
            }

            // stdout y stderr a la vez: leerlos en secuencia se bloquea cuando el
            // que no se lee llena su bufer.
            var sb = new StringBuilder();
            p.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                lock (sb) sb.AppendLine(e.Data);
                var t = e.Data.Trim();
                if (t.Length > 0 && (t.Contains("Downloading") || t.Contains("Success") ||
                                     t.Contains("ERROR") || t.Contains("Validating")))
                    avance?.Invoke(t);
            };
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) { lock (sb) sb.AppendLine(e.Data); }
            };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            if (!p.WaitForExit(900_000))
            {
                try { p.Kill(true); } catch { }
                log.AppendLine("SteamCMD tardó demasiado y se canceló.");
            }

            string salida;
            lock (sb) salida = sb.ToString();

            // Un item puede venir ya descargado ("Validating") o bajarse ahora
            // ("Downloading"/"Success"): los dos casos cuentan como disponibles.
            foreach (var id in lista)
            {
                bool bien = salida.Contains($"Success. Downloaded item {id}", StringComparison.Ordinal)
                         || Regex.IsMatch(salida, $@"Validating active workshop item {id}\b");
                if (bien) { ok.Add(id); log.AppendLine($"{id}: listo"); }
                else log.AppendLine($"{id}: no se pudo descargar");
            }

            var errores = salida.Split('\n')
                                .Where(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                                .Select(l => l.Trim()).Distinct().Take(5);
            foreach (var e2 in errores) log.AppendLine("  " + e2);
        }
        catch (Exception ex)
        {
            log.AppendLine("Error al ejecutar SteamCMD: " + ex.Message);
        }

        return (ok, log.ToString());
    }

    /// <summary>
    /// Carpeta de trabajo de SteamCMD. Se deduce del ejecutable, pero si el archivo
    /// configurado no existe se prueba la ubicacion habitual.
    /// </summary>
    public static string DirectorioTrabajo
    {
        get
        {
            if (File.Exists(Rim.Cfg.SteamCmd))
                return Path.GetDirectoryName(Rim.Cfg.SteamCmd) ?? Rim.Cfg.SteamCmd;

            foreach (var c in new[]
            {
                @"C:\WorkshopDL\steamcmd",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "steamcmd"),
            })
                if (Directory.Exists(c)) return c;

            return Path.GetDirectoryName(Rim.Cfg.SteamCmd) ?? ".";
        }
    }

    /// <summary>Carpeta donde SteamCMD deja los items del Workshop de RimWorld.</summary>
    public static string ContentDir =>
        Path.Combine(DirectorioTrabajo, "steamapps", "workshop", "content", "294100");

    /// <summary>
    /// Copia un item descargado a la carpeta Mods. Si ya existe un mod con el mismo
    /// packageId, lo trata como actualizacion y guarda copia.
    /// </summary>
    public static (bool ok, string msg) Instalar(string id, bool sobrescribir = true)
    {
        var src = Path.Combine(ContentDir, id);
        if (!Directory.Exists(src)) return (false, $"no se encontró el contenido descargado ({id})");

        // localizar el About.xml (puede estar en la raíz o en una subcarpeta)
        var raiz = src;
        if (!File.Exists(Path.Combine(raiz, "About", "About.xml")))
        {
            var cand = Directory.GetDirectories(src)
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "About", "About.xml")));
            if (cand != null) raiz = cand;
        }
        if (!File.Exists(Path.Combine(raiz, "About", "About.xml")))
            return (false, "el contenido descargado no tiene About.xml");

        string nombre, pk;
        try
        {
            var doc = XDocument.Load(Path.Combine(raiz, "About", "About.xml"));
            nombre = (doc.Root?.Element("name")?.Value ?? id).Trim();
            pk = (doc.Root?.Element("packageId")?.Value ?? "").Trim();
        }
        catch (Exception ex) { return (false, "About.xml ilegible: " + ex.Message); }

        // nombre de carpeta seguro
        foreach (var c in Path.GetInvalidFileNameChars()) nombre = nombre.Replace(c, '_');
        var destino = Path.Combine(Rim.ModsDir, nombre);

        // ya existe una carpeta con ese packageId?
        var existente = BuscarPorPackageId(pk);
        if (existente != null) destino = existente;

        try
        {
            if (Directory.Exists(destino))
            {
                if (!sobrescribir) return (false, "ya existe (no se sobrescribe)");
                var bak = destino + "__" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                Directory.Move(destino, bak);
            }
            Copiar(raiz, destino);

            if (!File.Exists(Path.Combine(destino, "About", "About.xml")))
                return (false, "quedó sin About.xml tras copiar");

            return (true, $"{nombre}  (versiones: {Versiones(destino)})");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    static string Versiones(string dir)
    {
        try
        {
            var doc = XDocument.Load(Path.Combine(dir, "About", "About.xml"));
            var v = doc.Root?.Element("supportedVersions")?.Elements("li").Select(x => x.Value.Trim());
            return v == null ? "?" : string.Join(",", v);
        }
        catch { return "?"; }
    }

    static void Copiar(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        foreach (var d in Directory.GetDirectories(src))
            Copiar(d, Path.Combine(dst, Path.GetFileName(d)));
    }

    /// <summary>Carpeta instalada que corresponde a un packageId, o null.</summary>
    public static string? BuscarPorPackageId(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId) || !Directory.Exists(Rim.ModsDir)) return null;
        foreach (var d in Directory.GetDirectories(Rim.ModsDir))
        {
            var about = Path.Combine(d, "About", "About.xml");
            if (!File.Exists(about)) continue;
            try
            {
                var doc = XDocument.Load(about);
                var pk = (doc.Root?.Element("packageId")?.Value ?? "").Trim();
                if (string.Equals(pk, packageId, StringComparison.OrdinalIgnoreCase)) return d;
            }
            catch { }
        }
        return null;
    }

    // ------------------------------------------------------------------ busqueda web

    /// <summary>
    /// Busca en el Workshop leyendo la pagina publica del taller, SIN clave de API.
    ///
    /// La API oficial (IPublishedFileService/QueryFiles) exige clave, pero la pagina
    /// de busqueda del taller es HTML normal y no pide nada. Devuelve id y titulo.
    /// </summary>
    public static List<(string Id, string Titulo)> BuscarWeb(string texto, int pagina = 1)
    {
        var res = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(texto)) return res;

        var url = "https://steamcommunity.com/workshop/browse/?appid=294100"
                + "&searchtext=" + Uri.EscapeDataString(texto)
                + "&browsesort=textsearch&section=readytouseitems&p=" + pagina;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/120.0 Safari/537.36");

            var html = http.GetStringAsync(url).GetAwaiter().GetResult();

            // cada resultado es un <a href="...filedetails/?id=N"> y dentro va la
            // miniatura, cuyo alt es el titulo del item
            foreach (Match m in Regex.Matches(html,
                @"filedetails/\?id=(\d{6,})""[^>]*>(.{0,600}?)</a>",
                RegexOptions.Singleline))
            {
                var id = m.Groups[1].Value;
                var titulo = "";
                var alt = Regex.Match(m.Groups[2].Value, @"alt=""([^""]*)""");
                if (alt.Success) titulo = System.Net.WebUtility.HtmlDecode(alt.Groups[1].Value);

                if (!res.Any(x => x.Item1 == id)) res.Add((id, titulo.Trim()));
            }
        }
        catch { }
        return res;
    }

    /// <summary>
    /// Normaliza un titulo para comparar: minusculas, sin prefijos [XYZ], sin
    /// (Continued), sin [1.6], sin simbolos.
    ///
    /// Los prefijos tipo "[RJW] " estropean la busqueda: Steam no los usa en el
    /// titulo del item, asi que buscarlos literalmente no encuentra nada.
    /// </summary>
    public static string ClaveTitulo(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var x = texto.ToLowerInvariant();
        x = Regex.Replace(x, @"^(\[[^\]]+\]\s*)+", "");
        x = x.Replace("(continued)", "").Replace("[continued]", "");
        x = Regex.Replace(x, @"\[[0-9\.]+\]", "");
        x = Regex.Replace(x, @"\([0-9\.]+\)", "");
        x = Regex.Replace(x, @"[^a-z0-9]", "");
        return x;
    }

    /// <summary>Texto de busqueda limpio: sin prefijos ni (Continued), con espacios.</summary>
    public static string LimpiarParaBuscar(string nombre)
    {
        var x = Regex.Replace(nombre, @"^(\[[^\]]+\]\s*)+", "");
        x = Regex.Replace(x, @"\(continued\)", "", RegexOptions.IgnoreCase);
        x = Regex.Replace(x, @"\[continued\]", "", RegexOptions.IgnoreCase);
        x = x.Trim();
        return string.IsNullOrWhiteSpace(x) ? nombre : x;
    }

    public record Vinculo(string Carpeta, string NombreMod, string Id, string TituloWeb, int Puntaje);

    /// <summary>Motivo por el que un mod no se vincula, para poder explicarlo.</summary>
    public record Rechazo(string Carpeta, string NombreMod, string Motivo);

    /// <summary>
    /// Un nombre demasiado corto o de una sola palabra no basta para identificar un
    /// mod: "[RJW] Insects" dio como "coincidencia exacta" el mod "Insects!" de 2017,
    /// que es otro completamente distinto. Vincular mal es peor que no vincular:
    /// una actualizacion futura sustituiria el mod por otro.
    ///
    /// Se mira el nombre YA LIMPIO pero CON espacios: contar palabras sobre la clave
    /// sin espacios daba una sola "palabra" siempre y rechazaba todo.
    /// </summary>
    public static bool NombreIdentificable(string nombreLimpio)
    {
        var letras = Regex.Matches(nombreLimpio.ToLowerInvariant(), @"[a-z0-9]").Count;
        if (letras < 12) return false;

        var palabras = Regex.Matches(nombreLimpio.ToLowerInvariant(), @"[a-z0-9]{3,}");
        return palabras.Count >= 2;
    }

    /// <summary>
    /// Datos basicos de varios items de una vez. No necesita clave: usa
    /// ISteamRemoteStorage/GetPublishedFileDetails, que es publico.
    /// </summary>
    public static Dictionary<string, (string Titulo, int Suscriptores)> Detalles(IEnumerable<string> ids)
    {
        var res = new Dictionary<string, (string, int)>(StringComparer.OrdinalIgnoreCase);
        var lista = ids.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(50).ToList();
        if (lista.Count == 0) return res;

        try
        {
            var form = new Dictionary<string, string> { { "itemcount", lista.Count.ToString() } };
            for (int i = 0; i < lista.Count; i++) form[$"publishedfileids[{i}]"] = lista[i];

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
            var resp = http.PostAsync(
                "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                new FormUrlEncodedContent(form)).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return res;

            var txt = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(txt);
            if (!doc.RootElement.TryGetProperty("response", out var r)) return res;
            if (!r.TryGetProperty("publishedfiledetails", out var arr)) return res;

            foreach (var f in arr.EnumerateArray())
            {
                if (f.TryGetProperty("result", out var rv) && rv.TryGetInt32(out var rc) && rc != 1) continue;
                var id = f.TryGetProperty("publishedfileid", out var p) ? (p.GetString() ?? "") : "";
                if (id.Length == 0) continue;
                var titulo = f.TryGetProperty("title", out var t) ? (t.GetString() ?? "") : "";
                var subs = f.TryGetProperty("subscriptions", out var s) && s.TryGetInt32(out var si) ? si : 0;
                res[id] = (titulo, subs);
            }
        }
        catch { }
        return res;
    }

    public static Vinculo? ProponerVinculo(string carpeta, string nombreMod)
        => AnalizarVinculo(carpeta, nombreMod, out _);

    /// <summary>
    /// Propone un id del Workshop comparando titulos, explicando el motivo si no vale.
    ///
    /// Solo acepta coincidencia EXACTA de la clave normalizada, y solo si el nombre
    /// es lo bastante especifico. Si varios items comparten titulo se desempata con
    /// los suscriptores reales; si sigue habiendo empate, no se vincula.
    /// </summary>
    public static Vinculo? AnalizarVinculo(string carpeta, string nombreMod, out Rechazo? rechazo)
    {
        rechazo = null;
        var clave = ClaveTitulo(nombreMod);
        if (clave.Length < 4)
        {
            rechazo = new Rechazo(carpeta, nombreMod, "nombre demasiado corto para buscar");
            return null;
        }

        var limpio = LimpiarParaBuscar(nombreMod);
        if (!NombreIdentificable(limpio))
        {
            rechazo = new Rechazo(carpeta, nombreMod,
                $"«{limpio}» es demasiado genérico (una palabra o muy corto)");
            return null;
        }

        var resultados = BuscarWeb(limpio);
        if (resultados.Count == 0)
        {
            rechazo = new Rechazo(carpeta, nombreMod, "sin resultados en el taller");
            return null;
        }

        var exactos = resultados.Where(x => ClaveTitulo(x.Titulo) == clave).ToList();
        if (exactos.Count == 0)
        {
            rechazo = new Rechazo(carpeta, nombreMod, "ningún título del taller coincide exactamente");
            return null;
        }

        if (exactos.Count == 1)
            return new Vinculo(carpeta, nombreMod, exactos[0].Id, exactos[0].Titulo, 100);

        // varios con el mismo titulo normalizado: desempatar con datos reales.
        // El titulo del taller suele traer sufijos ("(1.6)", "zh-pack"), asi que se
        // prefiere el que coincida letra a letra y, si no, el mas suscrito.
        var detalles = Detalles(exactos.Select(x => x.Id));
        var candidatos = exactos
            .Select(x => new
            {
                x.Id,
                x.Titulo,
                Bruto = detalles.TryGetValue(x.Id, out var d) ? d.Titulo : x.Titulo,
                Subs = detalles.TryGetValue(x.Id, out var d2) ? d2.Suscriptores : 0,
            })
            .ToList();

        // 1) coincidencia literal del titulo con el nombre del mod
        var literal = candidatos
            .Where(c => string.Equals(c.Bruto.Trim(), nombreMod.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (literal.Count == 1)
            return new Vinculo(carpeta, nombreMod, literal[0].Id, literal[0].Bruto, 100);

        // 2) el mas suscrito, si destaca sobre el segundo
        var orden = candidatos.OrderByDescending(c => c.Subs).ToList();
        if (orden.Count >= 2 && orden[0].Subs > 0 && orden[0].Subs >= orden[1].Subs * 2)
            return new Vinculo(carpeta, nombreMod, orden[0].Id, orden[0].Bruto, 100);

        var resumen = string.Join(", ", orden.Select(c => $"{c.Id}({c.Subs})"));
        rechazo = new Rechazo(carpeta, nombreMod,
            $"{exactos.Count} candidatos y ninguno destaca: {resumen}");
        return null;
    }

    /// <summary>Escribe About\PublishedFileId.txt, igual que hace el juego al descargar.</summary>
    public static bool EscribirPublishedFileId(string carpeta, string id, out string error)
    {
        error = "";
        try
        {
            var dir = Path.Combine(Rim.ModsDir, carpeta, "About");
            if (!Directory.Exists(dir)) { error = "no existe " + dir; return false; }
            File.WriteAllText(Path.Combine(dir, "PublishedFileId.txt"), id);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    /// <summary>
    /// Recorre los mods sin PublishedFileId.txt y propone vinculos.
    /// <paramref name="avance"/> recibe una linea por mod para mostrar progreso.
    /// </summary>
    public static List<Vinculo> BuscarVinculos(Action<string>? avance = null,
                                               List<Rechazo>? rechazos = null, int maximo = 80)
    {
        var res = new List<Vinculo>();
        if (!Directory.Exists(Rim.ModsDir)) return res;

        var pendientes = new List<(string carpeta, string nombre)>();
        foreach (var d in Directory.GetDirectories(Rim.ModsDir))
        {
            var carpeta = Path.GetFileName(d);
            if (carpeta.StartsWith("_")) continue;
            if (File.Exists(Path.Combine(d, "About", "PublishedFileId.txt"))) continue;

            var nombre = carpeta;
            try
            {
                var about = Path.Combine(d, "About", "About.xml");
                if (File.Exists(about))
                {
                    var doc = XDocument.Load(about);
                    var n = doc.Root?.Element("name")?.Value?.Trim();
                    if (!string.IsNullOrWhiteSpace(n)) nombre = n!;
                }
            }
            catch { }
            pendientes.Add((carpeta, nombre));
        }

        int hechos = 0;
        foreach (var (carpeta, nombre) in pendientes)
        {
            if (hechos >= maximo) break;
            hechos++;
            avance?.Invoke($"({hechos}/{Math.Min(pendientes.Count, maximo)}) {nombre}");

            var v = AnalizarVinculo(carpeta, nombre, out var rechazo);
            if (v != null) res.Add(v);
            else if (rechazo != null) rechazos?.Add(rechazo);

            // sin pausa Steam empieza a devolver paginas vacias
            Thread.Sleep(350);
        }
        return res;
    }

    // ------------------------------------------------------------------ colecciones

    /// <summary>
    /// Resuelve una coleccion del Workshop en los ids que contiene.
    ///
    /// Una coleccion no es un mod: SteamCMD no la descarga. Hay que pedir sus hijos
    /// a la API y descargar cada uno. Sin esto, pegar el enlace de una coleccion
    /// fallaba con un error de SteamCMD que no explicaba nada.
    ///
    /// No necesita clave de API.
    /// </summary>
    public static (List<string> hijos, string error) HijosDeColeccion(string id)
    {
        var res = new List<string>();
        if (string.IsNullOrWhiteSpace(id) || !Regex.IsMatch(id, @"^\d{6,}$"))
            return (res, "el id no parece válido");

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string,string>("collectioncount", "1"),
                new KeyValuePair<string,string>("publishedfileids[0]", id),
            });

            using var resp = http.PostAsync(
                "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/", body)
                .GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return (res, "Steam respondió " + (int)resp.StatusCode);

            var txt = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(txt);
            if (!doc.RootElement.TryGetProperty("response", out var r)) return (res, "respuesta sin datos");
            if (!r.TryGetProperty("collectiondetails", out var cd)) return (res, "no es una colección");
            if (cd.GetArrayLength() == 0) return (res, "no es una colección");

            var primera = cd[0];
            // result: 1 = existe; 9 = no existe
            if (primera.TryGetProperty("result", out var rv) && rv.TryGetInt32(out var rc) && rc != 1)
                return (res, "la colección no existe o es privada");

            if (!primera.TryGetProperty("children", out var hijos))
                return (res, "la colección está vacía");

            foreach (var h in hijos.EnumerateArray())
                if (h.TryGetProperty("publishedfileid", out var pf))
                {
                    var s = pf.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) res.Add(s);
                }

            return (res, res.Count == 0 ? "la colección está vacía" : "");
        }
        catch (Exception ex) { return (res, ex.Message); }
    }

    /// <summary>
    /// Si el id es una coleccion devuelve sus mods; si es un mod suelto, lo devuelve
    /// tal cual. El texto de <paramref name="nota"/> explica cual de las dos cosas fue.
    /// </summary>
    public static List<string> Expandir(IEnumerable<string> ids, out string nota)
    {
        var res = new List<string>();
        var notas = new List<string>();

        foreach (var id in ids)
        {
            var (hijos, err) = HijosDeColeccion(id);
            if (hijos.Count > 0)
            {
                notas.Add($"El id {id} es una colección: se añaden {hijos.Count} mods.");
                foreach (var h in hijos) if (!res.Contains(h)) res.Add(h);
            }
            else
            {
                if (!res.Contains(id)) res.Add(id);
            }
        }

        nota = string.Join("\r\n", notas);
        return res;
    }
// ------------------------------------------------------------------ actualizaciones

    public class Desactualizado
    {
        public string Titulo = "";
        public string Id = "";
        public string Carpeta = "";
        public DateTime Taller;
        public DateTime Local;
        public int Dias => (int)(Taller - Local).TotalDays;
    }

    /// <summary>
    /// Compara la fecha del Workshop con la del contenido instalado.
    /// Usa la fecha del ARCHIVO mas reciente dentro de la carpeta en lugar de la
    /// fecha de la carpeta: es mas fiable tras copias.
    /// </summary>
    public static List<Desactualizado> BuscarActualizaciones(out int comprobables)
    {
        comprobables = 0;
        var res = new List<Desactualizado>();
        var idx = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // id -> carpeta
        var fechaLocal = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        foreach (var d in Directory.GetDirectories(Rim.ModsDir))
        {
            if (Path.GetFileName(d).StartsWith("_")) continue;
            var f = Path.Combine(d, "About", "PublishedFileId.txt");
            if (!File.Exists(f)) continue;
            var id = File.ReadAllText(f).Trim();
            if (id.Length < 6 || !id.All(char.IsDigit)) continue;
            idx[id] = d;

            try
            {
                fechaLocal[id] = Directory.GetFiles(d, "*", SearchOption.AllDirectories)
                                          .Select(x => File.GetLastWriteTime(x))
                                          .DefaultIfEmpty(Directory.GetLastWriteTime(d))
                                          .Max();
            }
            catch { fechaLocal[id] = Directory.GetLastWriteTime(d); }
        }

        comprobables = idx.Count;
        if (comprobables == 0) return res;

        var ids = idx.Keys.ToList();
        for (int i = 0; i < ids.Count; i += 100)
        {
            var lote = ids.Skip(i).Take(100).ToList();
            var form = new Dictionary<string, string> { { "itemcount", lote.Count.ToString() } };
            for (int k = 0; k < lote.Count; k++) form[$"publishedfileids[{k}]"] = lote[k];

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
                var resp = http.PostAsync("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
                                          new FormUrlEncodedContent(form)).Result;
                if (!resp.IsSuccessStatusCode) continue;

                using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);
                foreach (var f in doc.RootElement.GetProperty("response").GetProperty("publishedfiledetails").EnumerateArray())
                {
                    var id = f.TryGetProperty("publishedfileid", out var p) ? p.GetString() ?? "" : "";
                    if (!idx.ContainsKey(id)) continue;
                    if (!f.TryGetProperty("time_updated", out var tu)) continue;
                    var taller = DateTimeOffset.FromUnixTimeSeconds(tu.GetInt64()).LocalDateTime;
                    var local = fechaLocal[id];
                    if (taller > local)
                        res.Add(new Desactualizado
                        {
                            Titulo = f.TryGetProperty("title", out var t) ? (t.GetString() ?? id) : id,
                            Id = id,
                            Carpeta = Path.GetFileName(idx[id]),
                            Taller = taller,
                            Local = local,
                        });
                }
            }
            catch { }
        }

        return res.OrderByDescending(x => x.Dias).ToList();
    }

    // ------------------------------------------------------------------ perfiles

    public static string PerfilesDir => Path.Combine(AppConfig.ExeDir, "perfiles");

    public static (bool ok, string msg) GuardarPerfil(string nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return (false, "falta el nombre");
        try
        {
            Directory.CreateDirectory(PerfilesDir);
            foreach (var c in Path.GetInvalidFileNameChars()) nombre = nombre.Replace(c, '_');
            var activos = Rim.ActiveMods();
            var perfil = new
            {
                nombre,
                creado = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                version = Rim.LocalVersion(),
                mods = activos,
            };
            var ruta = Path.Combine(PerfilesDir, nombre + ".json");
            File.WriteAllText(ruta, JsonSerializer.Serialize(perfil, new JsonSerializerOptions { WriteIndented = true }));
            return (true, $"{activos.Count} mods guardados en {Path.GetFileName(ruta)}");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public static List<(string archivo, string nombre, string creado, int mods)> ListarPerfiles()
    {
        var res = new List<(string, string, string, int)>();
        if (!Directory.Exists(PerfilesDir)) return res;
        foreach (var f in Directory.GetFiles(PerfilesDir, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(f));
                var r = doc.RootElement;
                res.Add((
                    Path.GetFileName(f),
                    r.TryGetProperty("nombre", out var n) ? (n.GetString() ?? "") : "",
                    r.TryGetProperty("creado", out var c) ? (c.GetString() ?? "") : "",
                    r.TryGetProperty("mods", out var m) ? m.GetArrayLength() : 0));
            }
            catch { }
        }
        return res.OrderBy(x => x.Item2).ToList();
    }

    public static (bool ok, string msg) CargarPerfil(string archivo)
    {
        try
        {
            var ruta = Path.IsPathRooted(archivo) ? archivo : Path.Combine(PerfilesDir, archivo);
            if (!File.Exists(ruta)) return (false, "no existe el perfil");

            using var doc = JsonDocument.Parse(File.ReadAllText(ruta));
            if (!doc.RootElement.TryGetProperty("mods", out var mods)) return (false, "el perfil no tiene lista de mods");

            var lista = mods.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToList();
            if (lista.Count == 0) return (false, "el perfil está vacío");

            if (!Rim.SetActiveMods(lista, out var bak, out var err)) return (false, err);

            // el perfil define el orden completo: pasa a ser la referencia
            Order.Guardar(lista);

            return (true, $"{lista.Count} mods activados. Copia: {Path.GetFileName(bak)}");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
