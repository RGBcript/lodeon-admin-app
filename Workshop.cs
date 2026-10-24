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
