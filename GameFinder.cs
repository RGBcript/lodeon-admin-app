using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace LodeonAdmin;

/// <summary>
/// Localiza la instalacion de RimWorld y de SteamCMD sin preguntar al usuario.
///
/// Tres vias, en orden de fiabilidad:
///   1. Una instalacion portable dentro de la propia carpeta del programa.
///   2. El registro de Steam, que da la raiz de Steam; desde ahi se leen
///      libraryfolders.vdf y cada appmanifest_294100.acf, que es el dato exacto
///      de donde Steam tiene instalado RimWorld.
///   3. Ubicaciones habituales, como ultimo recurso.
///
/// No basta con mirar "C:\Program Files (x86)\Steam\steamapps\common": mucha gente
/// tiene el juego en otra unidad o en una biblioteca secundaria.
/// </summary>
public static class GameFinder
{
    public const int AppId = 294100;

    public record Resultado(string? Juego, string? SteamCmd, string Explicacion);

    /// <summary>Busca la carpeta del juego. Devuelve null si no encuentra nada creible.</summary>
    public static string? BuscarJuego()
    {
        // 1) el ejecutable vive dentro del juego
        var sospechosa = AppConfig.ExeDir;
        var padre = Path.GetDirectoryName(sospechosa);
        if (padre != null && EsJuego(padre)) return padre;
        if (EsJuego(sospechosa)) return sospechosa;

        // 2) bibliotecas de Steam
        foreach (var raiz in RaicesSteam())
        {
            var directa = Path.Combine(raiz, "steamapps", "common", "RimWorld");
            if (EsJuego(directa)) return directa;

            foreach (var lib in Bibliotecas(raiz))
            {
                var ruta = Path.Combine(lib, "steamapps", "common", "RimWorld");
                if (EsJuego(ruta)) return ruta;
            }
        }

        // 3) ubicaciones conocidas
        foreach (var d in new[]
        {
            @"C:\Games\RimWorld",
            @"C:\RimWorld",
            @"D:\Games\RimWorld",
            @"D:\RimWorld",
            @"E:\Games\RimWorld",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                         "Steam", "steamapps", "common", "RimWorld"),
        })
            if (EsJuego(d)) return d;

        return null;
    }

    /// <summary>Comprueba que una carpeta sea de verdad una instalacion de RimWorld.</summary>
    public static bool EsJuego(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return false;
        try
        {
            if (File.Exists(Path.Combine(dir, "RimWorld.exe"))) return true;
            if (File.Exists(Path.Combine(dir, "RimWorldWin64.exe"))) return true;
            // instalaciones sin ejecutable visible pero con los datos
            if (Directory.Exists(Path.Combine(dir, "Mods")) &&
                File.Exists(Path.Combine(dir, "Version.txt"))) return true;
        }
        catch { }
        return false;
    }

    /// <summary>Raices de Steam segun el registro y las rutas de siempre.</summary>
    public static List<string> RaicesSteam()
    {
        var res = new List<string>();

        void Add(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            var limpia = p.Replace('/', '\\').TrimEnd('\\');
            if (Directory.Exists(limpia) && !res.Contains(limpia, StringComparer.OrdinalIgnoreCase))
                res.Add(limpia);
        }

        // HKCU es la mas fiable (Steam la escribe al instalarse)
        foreach (var (hive, key, valor) in new[]
        {
            (Registry.CurrentUser,  @"Software\Valve\Steam",              "SteamPath"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam",  "InstallPath"),
            (Registry.LocalMachine, @"SOFTWARE\Valve\Steam",              "InstallPath"),
        })
        {
            try
            {
                using var k = hive.OpenSubKey(key);
                if (k?.GetValue(valor) is string s) Add(s);
            }
            catch { }
        }

        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
        return res;
    }

    /// <summary>
    /// Bibliotecas adicionales declaradas en libraryfolders.vdf. El formato es
    /// VDF: bloques con "path" "D:\\SteamLibrary".
    /// </summary>
    public static List<string> Bibliotecas(string raizSteam)
    {
        var res = new List<string>();
        var vdf = Path.Combine(raizSteam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) return res;
        try
        {
            var txt = File.ReadAllText(vdf);
            foreach (Match m in Regex.Matches(txt, @"""path""\s+""([^""]+)"""))
            {
                var p = m.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(p) && !res.Contains(p, StringComparer.OrdinalIgnoreCase))
                    res.Add(p);
            }
        }
        catch { }
        return res;
    }

    /// <summary>
    /// Busca SteamCMD. Primero junto al programa, luego en las ubicaciones donde
    /// lo dejan WorkshopDL y las guias habituales.
    /// </summary>
    public static string? BuscarSteamCmd()
    {
        var candidatos = new List<string>();
        try
        {
            candidatos.Add(Path.Combine(AppConfig.ExeDir, "steamcmd", "steamcmd.exe"));
            candidatos.Add(Path.Combine(AppConfig.ExeDir, "steamcmd.exe"));
        }
        catch { }
        candidatos.Add(@"C:\WorkshopDL\steamcmd\steamcmd.exe");
        candidatos.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "steamcmd", "steamcmd.exe"));
        candidatos.Add(@"C:\steamcmd\steamcmd.exe");
        candidatos.Add(@"D:\steamcmd\steamcmd.exe");

        foreach (var c in candidatos)
            if (File.Exists(c)) return c;
        return null;
    }

    /// <summary>Deteccion completa con explicacion legible de lo que se encontro.</summary>
    public static Resultado Detectar()
    {
        var juego = BuscarJuego();
        var cmd = BuscarSteamCmd();
        var sb = new System.Text.StringBuilder();

        if (juego != null) sb.AppendLine("Juego: " + juego);
        else sb.AppendLine("Juego: no encontrado");

        if (cmd != null) sb.AppendLine("SteamCMD: " + cmd);
        else sb.AppendLine("SteamCMD: no encontrado (solo afecta a las descargas)");

        var raices = RaicesSteam();
        sb.AppendLine(raices.Count > 0
            ? "Steam: " + string.Join(", ", raices)
            : "Steam: no instalado o no localizado");

        return new Resultado(juego, cmd, sb.ToString().TrimEnd());
    }
}
