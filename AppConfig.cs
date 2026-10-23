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
    public string SteamCmd { get; set; } = "";
    public string GamePath { get; set; } = "";
    public string VersionsFolder { get; set; } = "";
    public bool DetectarIdioma { get; set; } = true;

    /// <summary>
    /// true cuando las rutas las eligio el usuario. Si es false, al arrancar se
    /// vuelve a intentar la deteccion automatica: asi el programa se arregla solo
    /// si el usuario mueve la instalacion de sitio.
    /// </summary>
    public bool RutasManuales { get; set; } = false;

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
    [System.Text.Json.Serialization.JsonIgnore]
    public static string LoadedFrom { get; private set; } = "";
    [System.Text.Json.Serialization.JsonIgnore]
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

                // Completar rutas: primero lo que el usuario fijo a mano; si no hay
                // nada, detectar. Asi un usuario nuevo no acaba con rutas inventadas.
                CompletarRutas(cfg);
                return cfg;
            }
            catch (Exception ex) { LoadError += f + ": " + ex.Message + "\n"; }
        }

        var nuevo = new AppConfig();
        CompletarRutas(nuevo);
        nuevo.Save();
        return nuevo;
    }

    /// <summary>
    /// Rellena las rutas que falten o que ya no existan.
    ///
    /// Se respeta lo que el usuario haya fijado a mano (RutasManuales) salvo que la
    /// ruta haya dejado de existir, en cuyo caso se intenta detectar de nuevo y se
    /// avisa por <see cref="RutasCambiadas"/>.
    /// </summary>
    public static void CompletarRutas(AppConfig cfg)
    {
        RutasCambiadas = "";

        bool juegoOk = GameFinder.EsJuego(cfg.GamePath);
        if (!juegoOk)
        {
            var detectado = GameFinder.BuscarJuego();
            if (detectado != null)
            {
                if (!string.IsNullOrWhiteSpace(cfg.GamePath))
                    RutasCambiadas += $"El juego ya no está en {cfg.GamePath}. Se encontró en {detectado}.\n";
                cfg.GamePath = detectado;
                cfg.RutasManuales = false;
                juegoOk = true;
            }
        }

        if (!File.Exists(cfg.SteamCmd))
        {
            var cmd = GameFinder.BuscarSteamCmd();
            if (cmd != null) cfg.SteamCmd = cmd;
            else if (string.IsNullOrWhiteSpace(cfg.SteamCmd))
                cfg.SteamCmd = @"C:\WorkshopDL\steamcmd\steamcmd.exe";
        }

        if (string.IsNullOrWhiteSpace(cfg.VersionsFolder) && juegoOk)
        {
            // junto al juego, no dentro: C:\Games\RimWorld -> C:\Games\Versiones
            var padre = Path.GetDirectoryName(cfg.GamePath.TrimEnd('\\'));
            if (!string.IsNullOrEmpty(padre))
                cfg.VersionsFolder = Path.Combine(padre, "Versiones");
        }
    }

    /// <summary>Mensaje si la deteccion cambio una ruta que ya no servia.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public static string RutasCambiadas { get; private set; } = "";

    /// <summary>true si hay una carpeta de juego utilizable.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool JuegoValido => GameFinder.EsJuego(GamePath);

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
