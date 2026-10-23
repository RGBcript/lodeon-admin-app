using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace LodeonAdmin;

/// <summary>
/// Descarga e instalacion desde el Workshop por SteamCMD, actualizacion de mods
/// instalados y perfiles de lista. Recupera lo que existia en la version PowerShell.
/// </summary>
public static class Workshop
{
    // ------------------------------------------------------------------ SteamCMD

    /// <summary>Descarga uno o varios items. Devuelve los ids que se bajaron bien.</summary>
    public static (List<string> ok, string log) Descargar(IEnumerable<string> ids)
    {
        var lista = ids.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct().ToList();
        var ok = new List<string>();
        var log = new StringBuilder();

        if (!File.Exists(Rim.Cfg.SteamCmd))
        {
            log.AppendLine("No se encuentra steamcmd en: " + Rim.Cfg.SteamCmd);
            return (ok, log.ToString());
        }

        foreach (var id in lista)
        {
            try
            {
                var psi = new ProcessStartInfo(File.Exists(Rim.Cfg.SteamCmd) ? Rim.Cfg.SteamCmd : "steamcmd.exe")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                // La descarga va a la carpeta de trabajo de SteamCMD, que es el sitio
                // donde despues la busca ContentDir. No se usa la carpeta del exe:
                // SteamCMD instala en "<carpeta de steamcmd>/steamapps/...".
                psi.ArgumentList.Add("+force_install_dir");
                psi.ArgumentList.Add(DirectorioTrabajo);
                psi.ArgumentList.Add("+login");
                psi.ArgumentList.Add("anonymous");
                psi.ArgumentList.Add("+workshop_download_item");
                psi.ArgumentList.Add("294100");
                psi.ArgumentList.Add(id);
                psi.ArgumentList.Add("+quit");

                using var p = Process.Start(psi);
                if (p == null) { log.AppendLine(id + ": no se pudo iniciar steamcmd"); continue; }

                // Leer stdout y stderr a la vez. Leerlos en secuencia puede bloquearse
                // cuando uno llena su bufer mientras el otro se lee.
                var sb = new StringBuilder();
                p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                if (!p.WaitForExit(600_000))
                {
                    try { p.Kill(true); } catch { }
                    log.AppendLine($"{id}: tiempo agotado, se canceló");
                    continue;
                }

                string salida;
                lock (sb) salida = sb.ToString();

                if (salida.Contains($"Success. Downloaded item {id}"))
                {
                    ok.Add(id);
                    log.AppendLine($"{id}: descargado");
                }
                else
                {
                    var linea = salida.Split('\n').FirstOrDefault(l => l.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                                       ?.Trim();
                    log.AppendLine($"{id}: fallo" + (linea != null ? " — " + linea : ""));
                }
            }
            catch (Exception ex) { log.AppendLine($"{id}: {ex.Message}"); }
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

            return (true, $"{lista.Count} mods activados. Copia: {Path.GetFileName(bak)}");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
