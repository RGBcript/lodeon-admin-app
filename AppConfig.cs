using System.Text.Json;

namespace LodeonAdmin;

/// <summary>
/// Configuracion persistente.
///
/// IMPORTANTE: en un ejecutable de archivo unico, AppContext.BaseDirectory apunta a
/// la carpeta temporal de extraccion, NO a la carpeta del .exe. Por eso aqui se usa
/// siempre la ruta del ejecutable real, con varios respaldos.
/// </summary>
public class AppConfig
{
    public string SteamApiKey { get; set; } = "";
    public string SteamCmd { get; set; } = @"C:\WorkshopDL\steamcmd\steamcmd.exe";
    public string GamePath { get; set; } = @"C:\Games\RimWorld";
    public string VersionsFolder { get; set; } = @"C:\Games\Versiones";
    public bool DetectarIdioma { get; set; } = true;

    /// <summary>Avisar al arrancar si hay mods desactualizados en el Workshop.</summary>
    public bool AvisarActualizaciones { get; set; } = true;

    /// <summary>Ultima fecha en que se mostro el aviso, para no repetirlo cada arranque.</summary>
    public string UltimoAvisoActualizaciones { get; set; } = "";

    /// <summary>Carpeta donde vive realmente el .exe.</summary>
    public static string ExeDir
    {
        get
        {
            try
            {
                var p = Environment.ProcessPath;          // ruta real del .exe
                if (!string.IsNullOrEmpty(p)) return Path.GetDirectoryName(p)!;
            }
            catch { }
            return AppContext.BaseDirectory;
        }
    }

    public static string FilePath => Path.Combine(ExeDir, "config.json");

    /// <summary>Ultima ruta de la que se cargo (para diagnostico).</summary>
    public static string LoadedFrom { get; private set; } = "";
    public static string LoadError { get; private set; } = "";

    /// <summary>Candidatos en orden de preferencia.</summary>
    private static IEnumerable<string> Candidates()
    {
        yield return Path.Combine(ExeDir, "config.json");
        if (!string.Equals(ExeDir, AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
            yield return Path.Combine(AppContext.BaseDirectory, "config.json");
        // ubicacion anterior del programa, por si el usuario movio el ejecutable
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "lodeon-admin", "config.json");
    }

    public static AppConfig Load()
    {
        LoadError = "";

        foreach (var f in Candidates())
        {
            try
            {
                if (!File.Exists(f)) continue;
                var txt = File.ReadAllText(f);
                if (string.IsNullOrWhiteSpace(txt)) continue;

                var cfg = JsonSerializer.Deserialize<AppConfig>(txt,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (cfg == null) continue;

                LoadedFrom = f;

                // Si el archivo encontrado no trae clave pero otro candidato si,
                // quedarse con la clave buena: es el fallo que dejaba Workshop sin funcionar.
                if (string.IsNullOrWhiteSpace(cfg.SteamApiKey))
                {
                    foreach (var otro in Candidates())
                    {
                        if (otro == f || !File.Exists(otro)) continue;
                        try
                        {
                            var c2 = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(otro),
                                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (c2 != null && !string.IsNullOrWhiteSpace(c2.SteamApiKey))
                            {
                                cfg.SteamApiKey = c2.SteamApiKey;
                                break;
                            }
                        }
                        catch { }
                    }
                }

                // completar rutas vacias
                if (string.IsNullOrWhiteSpace(cfg.GamePath)) cfg.GamePath = @"C:\Games\RimWorld";
                if (string.IsNullOrWhiteSpace(cfg.SteamCmd)) cfg.SteamCmd = @"C:\WorkshopDL\steamcmd\steamcmd.exe";
                return cfg;
            }
            catch (Exception ex) { LoadError += f + ": " + ex.Message + "\n"; }
        }

        var nuevo = new AppConfig();
        nuevo.Save();
        return nuevo;
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch { }
    }
}
