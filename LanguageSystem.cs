using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LodeonAdmin;

/// <summary>
/// Sistema de idioma: rastrea qué idiomas existen en los mods, detecta el idioma
/// del usuario y elige la mejor carpeta disponible para cada mod.
///
/// El problema real que resuelve: RimWorld solo carga la carpeta cuyo nombre
/// coincide EXACTAMENTE con el idioma activo. Si un mod trae "Spanish" y el juego
/// usa "Spanish (Español(Castellano))", esa traduccion se ignora en silencio.
/// </summary>
public static class LanguageSystem
{
    // ------------------------------------------------------------------ modelo

    public class LangFolder
    {
        public string Mod = "";
        public string Path = "";
        public string FolderName = "";
        public string ViewPath = "";          // ruta de origen si es un enlace
        public bool IsLink;
        public bool IsDummy;                   // carpeta-puente que creamos nosotros
        public int FileCount;
    }

    public class ModLanguage
    {
        public string ModName = "";
        public string PackageId = "";
        public List<LangFolder> Folders = new();
        public bool Active;
    }

    public class Choice
    {
        public string ModName = "";
        public string FolderName = "";
        public string Reason = "";
        public ModLanguage? Source;
        public LangFolder? Pick;
    }

    // -------------------------------------------------- nombre canonico de idioma

    /// <summary>Normaliza las variantes de nombre a una clave interna.</summary>
    public static string Canon(string folder)
    {
        var f = folder.ToLowerInvariant();

        if (f.Contains("spanish (espa")) return "spanish";
        if (f.StartsWith("spanishlatin")) return "spanishlatin";
        if (f.Contains("spanish")) return "spanish";

        if (f.Contains("russian")) return "russian";
        if (f.Contains("chinesesimplified")) return "chinesesimp";
        if (f.Contains("chinesetraditional")) return "chinesetrad";
        if (f.Contains("portuguesebrazilian")) return "portbrazil";
        if (f.Contains("portuguese")) return "portuguese";
        if (f.Contains("french")) return "french";
        if (f.Contains("german")) return "german";
        if (f.Contains("korean")) return "korean";
        if (f.Contains("japanese")) return "japanese";
        if (f.Contains("polish")) return "polish";
        if (f.Contains("italian")) return "italian";
        if (f.Contains("czech")) return "czech";
        if (f.Contains("ukrainian")) return "ukrainian";
        if (f.Contains("turkish")) return "turkish";
        if (f.Contains("dutch")) return "dutch";
        if (f.Contains("swedish")) return "swedish";
        if (f.Contains("norwegian")) return "norwegian";
        if (f.Contains("danish")) return "danish";
        if (f.Contains("finnish")) return "finnish";
        if (f.Contains("hungarian")) return "hungarian";
        if (f.Contains("romanian")) return "romanian";
        if (f.Contains("thai")) return "thai";
        if (f.Contains("vietnamese")) return "vietnamese";
        if (f.Contains("indonesian")) return "indonesian";
        if (f.Contains("arabic")) return "arabic";
        if (f.Contains("hebrew")) return "hebrew";
        if (f.StartsWith("english")) return "english";

        return f;
    }

    /// <summary>Nombre de la carpeta que espera RimWorld para esa clave.</summary>
    public static string PreferredFolderName(string canon) => canon switch
    {
        "spanish" => "Spanish (Español(Castellano))",
        "spanishlatin" => "SpanishLatin (Español(Latinoamérica))",
        "russian" => "Russian (Русский)",
        "chinesesimp" => "ChineseSimplified (简体中文)",
        "chinesetrad" => "ChineseTraditional (繁體中文)",
        "portbrazil" => "PortugueseBrazilian (Português Brasileiro)",
        "korean" => "Korean (한국어)",
        "japanese" => "Japanese (日本語)",
        "french" => "French (Français)",
        "german" => "German (Deutsch)",
        "polish" => "Polish (Polski)",
        "italian" => "Italian (Italiano)",
        "czech" => "Czech (Čeština)",
        "ukrainian" => "Ukrainian (Українська)",
        "turkish" => "Turkish (Türkçe)",
        "portuguese" => "Portuguese (Português)",
        "english" => "English",
        _ => canon
    };

    // ------------------------------------------------------------- deteccion

    /// <summary>Idioma del usuario segun Windows, traducido a clave interna.</summary>
    public static (string canon, string detalle) DetectUserLanguage()
    {
        try
        {
            var ui = CultureInfo.InstalledUICulture;
            var dos = ui.TwoLetterISOLanguageName.ToLowerInvariant();
            var detalle = $"Windows: {ui.Name} ({ui.DisplayName})";
            var canon = dos switch
            {
                "es" => "spanish",
                "ru" => "russian",
                "zh" => ui.Name.Contains("TW") || ui.Name.Contains("HK") ? "chinesetrad" : "chinesesimp",
                "pt" => ui.Name.Contains("BR") ? "portbrazil" : "portuguese",
                "fr" => "french",
                "de" => "german",
                "ko" => "korean",
                "ja" => "japanese",
                "pl" => "polish",
                "it" => "italian",
                "cs" => "czech",
                "uk" => "ukrainian",
                "tr" => "turkish",
                "nl" => "dutch",
                "sv" => "swedish",
                _ => "english"
            };
            return (canon, detalle);
        }
        catch
        {
            return ("english", "no se pudo detectar");
        }
    }

    /// <summary>Idioma que el juego tiene configurado ahora mismo.</summary>
    public static (string canon, string folder) GameLanguage()
    {
        try
        {
            var p = Path.Combine(Rim.ProfileDir, "Config", "Prefs.xml");
            if (File.Exists(p))
            {
                var t = File.ReadAllText(p);
                var m = Regex.Match(t, "<langFolderName>([^<]+)</langFolderName>");
                if (m.Success)
                {
                    var folder = m.Groups[1].Value;
                    return (Canon(folder), folder);
                }
            }
        }
        catch { }
        return ("english", "English");
    }

    // ------------------------------------------------------------- inventario

    /// <summary>Todos los idiomas presentes, con cuantos mods ofrecen cada uno.</summary>
    public static Dictionary<string, int> Inventory(out List<ModLanguage> detalle)
    {
        detalle = ScanMods();
        var d = new Dictionary<string, int>();
        foreach (var m in detalle)
            foreach (var c in m.Folders.Select(f => Canon(f.FolderName)).Distinct())
                d[c] = d.TryGetValue(c, out var v) ? v + 1 : 1;
        return d;
    }

    /// <summary>Lee todas las carpetas de idioma de todos los mods instalados.</summary>
    public static List<ModLanguage> ScanMods()
    {
        var res = new List<ModLanguage>();
        var modsDir = Rim.ModsDir;
        if (!Directory.Exists(modsDir)) return res;

        var activos = new HashSet<string>(Rim.ActiveMods().Select(x => x.ToLowerInvariant()));

        foreach (var d in Directory.GetDirectories(modsDir))
        {
            var nombreCarpeta = Path.GetFileName(d);
            if (nombreCarpeta.StartsWith("_")) continue;
            var about = Path.Combine(d, "About", "About.xml");
            if (!File.Exists(about)) continue;

            string pk = "", nm = nombreCarpeta;
            try
            {
                var doc = XDocument.Load(about);
                pk = (doc.Root?.Element("packageId")?.Value ?? "").Trim();
                nm = (doc.Root?.Element("name")?.Value ?? nombreCarpeta).Trim();
            }
            catch { continue; }

            var ml = new ModLanguage
            {
                ModName = nm,
                PackageId = pk,
                Active = activos.Contains(pk.ToLowerInvariant())
            };

            void AddLangs(string baseDir)
            {
                var ldir = Path.Combine(baseDir, "Languages");
                if (!Directory.Exists(ldir)) return;
                foreach (var lf in Directory.GetDirectories(ldir))
                {
                    var info = new DirectoryInfo(lf);
                    var esEnlace = info.Attributes.HasFlag(FileAttributes.ReparsePoint);
                    var dummy = File.Exists(Path.Combine(lf, ".lodeon-bridge"));
                    ml.Folders.Add(new LangFolder
                    {
                        Mod = nombreCarpeta,
                        Path = lf,
                        FolderName = Path.GetFileName(lf),
                        IsLink = esEnlace,
                        IsDummy = dummy,
                        FileCount = CountXml(lf)
                    });
                }
            }

            AddLangs(d);
            foreach (var v in Directory.GetDirectories(d))
            {
                var vn = Path.GetFileName(v);
                if (Regex.IsMatch(vn, @"^(\d\.\d|v\d)")) AddLangs(v);
            }

            if (ml.Folders.Count > 0) res.Add(ml);
        }
        return res.OrderBy(x => x.ModName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static int CountXml(string dir)
    {
        try { return Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories).Length; }
        catch { return 0; }
    }

    // ------------------------------------------------------------- eleccion

    /// <summary>
    /// Elige, para cada mod, la mejor carpeta para el idioma pedido.
    /// Prioridad: carpeta exacta > variante equivalente > ingles > nada.
    /// </summary>
    public static List<Choice> Plan(string targetCanon, bool soloActivos = true)
    {
        var mods = ScanMods();
        var res = new List<Choice>();

        foreach (var m in mods)
        {
            if (soloActivos && !m.Active) continue;

            // 1) carpeta exacta
            var exacta = m.Folders.FirstOrDefault(f =>
                Canon(f.FolderName) == targetCanon && !f.IsDummy &&
                string.Equals(f.FolderName, PreferredFolderName(targetCanon), StringComparison.OrdinalIgnoreCase));
            if (exacta != null)
            {
                res.Add(new Choice { ModName = m.ModName, FolderName = exacta.FolderName, Reason = "exacta", Source = m, Pick = exacta });
                continue;
            }

            // 2) variante del mismo idioma
            var variante = m.Folders.Where(f => Canon(f.FolderName) == targetCanon && !f.IsDummy)
                                    .OrderByDescending(f => f.FileCount).FirstOrDefault();
            if (variante != null)
            {
                res.Add(new Choice { ModName = m.ModName, FolderName = variante.FolderName, Reason = "variante", Source = m, Pick = variante });
                continue;
            }

            // 3) ingles como respaldo
            var ingles = m.Folders.FirstOrDefault(f => Canon(f.FolderName) == "english" && !f.IsDummy);
            if (ingles != null)
            {
                res.Add(new Choice { ModName = m.ModName, FolderName = ingles.FolderName, Reason = "respaldo ingles", Source = m, Pick = ingles });
                continue;
            }

            res.Add(new Choice { ModName = m.ModName, FolderName = "", Reason = "sin idioma", Source = m, Pick = null });
        }
        return res;
    }

    /// <summary>Mods que ganarian con un puente: tienen el idioma pero con otro nombre.</summary>
    public static List<Choice> BridgeCandidates(string targetCanon)
    {
        var deseado = PreferredFolderName(targetCanon);
        var res = new List<Choice>();
        foreach (var m in ScanMods())
        {
            if (!m.Active) continue;
            var yaTiene = m.Folders.Any(f => string.Equals(f.FolderName, deseado, StringComparison.OrdinalIgnoreCase));
            if (yaTiene) continue;
            var v = m.Folders.Where(f => Canon(f.FolderName) == targetCanon && !f.IsDummy && !f.IsLink)
                             .OrderByDescending(f => f.FileCount).FirstOrDefault();
            if (v != null)
                res.Add(new Choice { ModName = m.ModName, FolderName = v.FolderName, Reason = $"puente -> {deseado}", Source = m, Pick = v });
        }
        return res;
    }

    // ------------------------------------------------------------- puentes

    public const string BridgeFlag = ".lodeon-bridge";

    /// <summary>
    /// Crea un enlace de directorio con el nombre que el juego espera, apuntando a
    /// la carpeta real del idioma. Asi no se duplica nada y el mod sigue intacto.
    /// Si no es posible crear el enlace, hace copia.
    /// </summary>
    public static (bool ok, string msg, bool enlace) CreateBridge(LangFolder origen, string canonDestino)
    {
        try
        {
            var nombreDeseado = PreferredFolderName(canonDestino);
            var langDir = Path.GetDirectoryName(origen.Path)!;      // ...\Languages
            var destino = Path.Combine(langDir, nombreDeseado);

            if (Directory.Exists(destino)) return (true, "ya existia", false);
            if (string.Equals(origen.FolderName, nombreDeseado, StringComparison.OrdinalIgnoreCase))
                return (true, "ya es la correcta", false);

            // 1) intento con enlace simbolico de directorio
            try
            {
                Directory.CreateSymbolicLink(destino, origen.Path);
                File.WriteAllText(Path.Combine(destino, BridgeFlag), origen.FolderName);
                return (true, "enlace", true);
            }
            catch
            {
                // 2) sin permisos de enlace: copiar
                CopyDir(origen.Path, destino);
                File.WriteAllText(Path.Combine(destino, BridgeFlag), origen.FolderName);
                return (true, "copia", false);
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message, false);
        }
    }

    static void CopyDir(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var f in Directory.GetFiles(src))
        {
            if (Path.GetFileName(f) == BridgeFlag) continue;
            File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
        }
        foreach (var d in Directory.GetDirectories(src))
        {
            var info = new DirectoryInfo(d);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }
    }

    /// <summary>Borra solo los puentes que creamos nosotros, nunca contenido original.</summary>
    public static int RemoveBridges()
    {
        int n = 0;
        foreach (var m in ScanMods())
            foreach (var f in m.Folders.Where(x => x.IsDummy))
            {
                try
                {
                    // un enlace se borra sin tocar el destino; una copia se borra entera
                    var info = new DirectoryInfo(f.Path);
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(f.Path, false);
                    else Directory.Delete(f.Path, true);
                    n++;
                }
                catch { }
            }
        return n;
    }

    /// <summary>Escribe el idioma en Prefs.xml del juego (conserva el resto del archivo).</summary>
    public static (bool ok, string msg) SetGameLanguage(string canon)
    {
        try
        {
            var prefs = Path.Combine(Rim.ProfileDir, "Config", "Prefs.xml");
            if (!File.Exists(prefs)) return (false, "No existe Prefs.xml (abre el juego una vez).");

            var texto = File.ReadAllText(prefs);
            var nombre = PreferredFolderName(canon);
            var backup = prefs + ".bak-lodeon-" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.Copy(prefs, backup, true);

            if (Regex.IsMatch(texto, "<langFolderName>[^<]*</langFolderName>"))
                texto = Regex.Replace(texto, "<langFolderName>[^<]*</langFolderName>",
                                      $"<langFolderName>{nombre}</langFolderName>");
            else
            {
                var i = texto.IndexOf("</PrefsData>", StringComparison.Ordinal);
                if (i < 0) return (false, "Prefs.xml no tiene el cierre esperado.");
                texto = texto.Insert(i, $"  <langFolderName>{nombre}</langFolderName>\r\n");
            }

            File.WriteAllText(prefs, texto);
            return (true, nombre);
        }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
