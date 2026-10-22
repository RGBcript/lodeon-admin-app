using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LodeonAdmin;

/// <summary>Toda la logica de RimWorld, sin nada de interfaz.</summary>
public static class Rim
{
    public static AppConfig Cfg = AppConfig.Load();

    /// <summary>Carpeta de datos del juego: %LOCALAPPDATA%Low\Ludeon Studios\RimWorld by Ludeon Studios</summary>
    public static string ProfileDir
    {
        get
        {
            var low = Environment.GetEnvironmentVariable("USERPROFILE") ?? "";
            return Path.Combine(low, "AppData", "LocalLow", "Ludeon Studios", "RimWorld by Ludeon Studios");
        }
    }

    public static string LogPath => Path.Combine(ProfileDir, "Logs", "Player.log");
    public static string ModsConfigPath => Path.Combine(ProfileDir, "Config", "ModsConfig.xml");
    public static string ModsDir => Path.Combine(Cfg.GamePath, "Mods");
    public static string VersionsDir => Path.Combine(Cfg.GamePath, "_backup-updates");
    public static string QuarantineDir => Path.Combine(Cfg.GamePath, "_ruso-cuarentena");

    // ---------------------------------------------------------------- version

    public static string LocalVersion()
    {
        try
        {
            var v = Path.Combine(Cfg.GamePath, "Version.txt");
            if (File.Exists(v)) return File.ReadAllText(v).Trim();
        }
        catch { }
        return "?";
    }

    public static string ActiveLanguage()
    {
        try
        {
            var p = Path.Combine(ProfileDir, "Config", "Prefs.xml");
            if (!File.Exists(p)) return "?";
            var t = File.ReadAllText(p);
            var m = Regex.Match(t, "<langFolderName>([^<]+)</langFolderName>");
            return m.Success ? m.Groups[1].Value : "?";
        }
        catch { return "?"; }
    }

    // ---------------------------------------------------------------- mods config

    public static List<string> ActiveMods()
    {
        var res = new List<string>();
        try
        {
            if (!File.Exists(ModsConfigPath)) return res;
            var doc = XDocument.Load(ModsConfigPath);
            var am = doc.Root?.Element("activeMods");
            if (am == null) return res;
            foreach (var li in am.Elements("li"))
            {
                var v = li.Value.Trim();
                if (!string.IsNullOrEmpty(v)) res.Add(v);
            }
        }
        catch { }
        return res;
    }

    /// <summary>Escribe activeMods conservando BOM y el resto del archivo intacto.</summary>
    public static bool SetActiveMods(IEnumerable<string> ids, out string backup, out string error)
    {
        backup = ""; error = "";
        try
        {
            var path = ModsConfigPath;
            if (!File.Exists(path)) { error = "No existe ModsConfig.xml"; return false; }

            backup = path + ".bak-lodeon-" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.Copy(path, backup, true);

            var raw = File.ReadAllText(path);
            int ini = raw.IndexOf("<activeMods>", StringComparison.Ordinal);
            int fin = raw.IndexOf("</activeMods>", StringComparison.Ordinal);
            if (ini < 0 || fin < ini) { error = "No encuentro el bloque activeMods"; return false; }

            var cab = raw.Substring(0, ini + "<activeMods>".Length);
            var cola = raw.Substring(fin);
            var sb = new StringBuilder();
            sb.Append("\r\n");
            foreach (var id in ids) sb.Append("    <li>").Append(id).Append("</li>\r\n");
            sb.Append("  ");

            var bytes = File.ReadAllBytes(path);
            bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            File.WriteAllText(path, cab + sb + cola, new UTF8Encoding(bom));

            var check = ActiveMods().Count;
            if (check != ids.Count()) { error = $"Verificacion fallida: escribi {ids.Count()} y hay {check}"; return false; }
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    // ---------------------------------------------------------------- estado del archivo

    public enum EstadoConfig { Ok, NoExiste, Corrupto }

    /// <summary>
    /// Comprueba si ModsConfig.xml se puede leer. Un archivo corrupto deja el juego
    /// sin cargar mods y no avisa: conviene detectarlo antes de escribir nada.
    /// </summary>
    public static (EstadoConfig estado, string detalle, int mods) EstadoModsConfig()
    {
        var p = ModsConfigPath;
        if (!File.Exists(p)) return (EstadoConfig.NoExiste, "No existe ModsConfig.xml", 0);
        try
        {
            var doc = XDocument.Load(p);
            var am = doc.Root?.Element("activeMods");
            if (am == null)
                return (EstadoConfig.Corrupto, "Falta el bloque <activeMods>", 0);
            int n = am.Elements("li").Count();
            if (n == 0)
                return (EstadoConfig.Corrupto, "La lista de mods está vacía", 0);
            return (EstadoConfig.Ok, "", n);
        }
        catch (Exception ex)
        {
            return (EstadoConfig.Corrupto, ex.Message, 0);
        }
    }

    /// <summary>Copias de ModsConfig mas recientes primero.</summary>
    public static List<string> CopiasModsConfig()
    {
        var res = new List<string>();
        try
        {
            var dir = Path.GetDirectoryName(ModsConfigPath) ?? "";
            if (!Directory.Exists(dir)) return res;
            res = Directory.GetFiles(dir, "ModsConfig.xml.bak*")
                           .OrderByDescending(f => File.GetLastWriteTime(f))
                           .ToList();
        }
        catch { }
        return res;
    }

    /// <summary>
    /// Restaura una copia. Antes guarda el archivo actual como .antes-de-restaurar
    /// para que la operacion se pueda deshacer.
    /// </summary>
    public static bool RestaurarModsConfig(string copia, out string error, out string rescate)
    {
        error = ""; rescate = "";
        try
        {
            if (!File.Exists(copia)) { error = "No existe la copia indicada"; return false; }

            var actual = ModsConfigPath;
            if (File.Exists(actual))
            {
                rescate = actual + ".antes-de-restaurar-" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Copy(actual, rescate, true);
            }
            File.Copy(copia, actual, true);

            var (estado, detalle, n) = EstadoModsConfig();
            if (estado != EstadoConfig.Ok)
            {
                error = "La copia tampoco es válida: " + detalle;
                return false;
            }
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    // ---------------------------------------------------------------- inventario de mods

    public record ModInfo(string PackageId, string Name, string Folder, bool Active, bool SinAbout = false);

    public static List<ModInfo> AllMods()
    {
        var lista = new List<ModInfo>();
        if (!Directory.Exists(ModsDir)) return lista;
        var activos = new HashSet<string>(ActiveMods().Select(x => x.ToLowerInvariant()));
        foreach (var d in Directory.GetDirectories(ModsDir))
        {
            var nombre = Path.GetFileName(d);
            if (nombre.StartsWith("_")) continue;
            var about = Path.Combine(d, "About", "About.xml");
            if (!File.Exists(about))
            {
                // Carpeta sin About.xml: el juego la ignora en silencio. Se incluye
                // marcada para que se vea, en vez de desaparecer del inventario.
                lista.Add(new ModInfo("", nombre, nombre, false, true));
                continue;
            }
            try
            {
                var doc = XDocument.Load(about);
                var r = doc.Root;
                if (r == null) continue;
                var pk = (r.Element("packageId")?.Value ?? "").Trim();
                if (pk.Length == 0) continue;
                var nm = (r.Element("name")?.Value ?? nombre).Trim();
                lista.Add(new ModInfo(pk, nm, nombre, activos.Contains(pk.ToLowerInvariant())));
            }
            catch { }
        }
        return lista.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------------------------------------------------------------- log

    public static string ReadLog()
    {
        try
        {
            if (!File.Exists(LogPath)) return "";
            using var fs = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }
        catch { return ""; }
    }

    public record LogStats(long Bytes, int Errors, int XmlErrors, int Warns, string State);

    public static LogStats Analyze()
    {
        var t = ReadLog();
        if (t.Length == 0) return new LogStats(0, 0, 0, 0, "sin log");
        int err = Regex.Matches(t, @"(?im)^\d{4}-\d\d-\d\d .*?\bERROR\b.*$").Count;
        int xml = Regex.Matches(t, @"(?i)xml error").Count;
        int wrn = Regex.Matches(t, @"(?im)^\d{4}-\d\d-\d\d .*?\bWARN\b.*$").Count;
        var estado = err == 0 ? "LIMPIO" : $"{err} errores";
        return new LogStats(t.Length, err, xml, wrn, estado);
    }

    // ---------------------------------------------------------------- proceso

    public static bool GameRunning()
    {
        try { return Process.GetProcessesByName("RimWorld").Length > 0; }
        catch { return false; }
    }

    public static bool LaunchGame(out string error)
    {
        error = "";
        try
        {
            var exe = Path.Combine(Cfg.GamePath, "RimWorld.exe");
            if (!File.Exists(exe)) { error = "No existe " + exe; return false; }
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Cfg.GamePath });
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    // ---------------------------------------------------------------- ruso

    public static List<string> RussianFolders()
    {
        var res = new List<string>();
        if (!Directory.Exists(ModsDir)) return res;
        foreach (var d in Directory.GetDirectories(ModsDir))
        {
            var n = Path.GetFileName(d);
            if (n.StartsWith("_")) continue;
            foreach (var sub in new[] { Path.Combine(d, "Languages", "Russian"), Path.Combine(d, "Languages", "Russian (Русский)") })
                if (Directory.Exists(sub)) res.Add(sub);
            foreach (var v in Directory.GetDirectories(d))
            {
                var vn = Path.GetFileName(v);
                if (Regex.IsMatch(vn, @"^(\d\.\d|v\d)"))
                    foreach (var sub in new[] { Path.Combine(v, "Languages", "Russian"), Path.Combine(v, "Languages", "Russian (Русский)") })
                        if (Directory.Exists(sub)) res.Add(sub);
            }
        }
        return res;
    }

    public static (int activas, int guardadas) RussianState()
    {
        int activas = RussianFolders().Count;
        int guardadas = 0;
        if (Directory.Exists(QuarantineDir))
            guardadas = Directory.GetDirectories(QuarantineDir, "Russian*", SearchOption.AllDirectories).Length;
        return (activas, guardadas);
    }

    public static int SetRussian(bool enabled)
    {
        int n = 0;
        if (enabled)
        {
            if (!Directory.Exists(QuarantineDir)) return 0;
            foreach (var f in Directory.GetDirectories(QuarantineDir, "Russian*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(QuarantineDir, f);
                var dest = Path.Combine(ModsDir, rel);
                if (!Directory.Exists(dest))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    Directory.Move(f, dest);
                    n++;
                }
            }
        }
        else
        {
            foreach (var f in RussianFolders())
            {
                var rel = Path.GetRelativePath(ModsDir, f);
                var dest = Path.Combine(QuarantineDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                Directory.Move(f, dest);
                n++;
            }
        }
        return n;
    }

    // ---------------------------------------------------------------- backups

    public static long DirSize(string dir)
    {
        try { return Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length); }
        catch { return 0; }
    }

    public static (long mb, int count) BackupsInfo()
    {
        long total = 0; int c = 0;
        if (Directory.Exists(VersionsDir))
            foreach (var d in Directory.GetDirectories(VersionsDir)) { total += DirSize(d); c++; }
        return (total / (1024 * 1024), c);
    }
}
