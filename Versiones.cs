using System.Diagnostics;

namespace LodeonAdmin;

/// <summary>
/// Version instalada de RimWorld, para poder abrir cualquiera de las copias
/// guardadas sin cambiar la configuracion.
/// </summary>
public record Instalacion(string Nombre, string Exe, string Version, bool Activa);

/// <summary>
/// Localiza las copias del juego en la carpeta de versiones y permite abrirlas.
///
/// Sirve para probar un modpack en 1.6 y comparar con 1.7, o para volver a una
/// version anterior sin reinstalar.
/// </summary>
public static class Versiones
{
    /// <summary>Todas las instalaciones encontradas: la activa primero.</summary>
    public static List<Instalacion> Listar()
    {
        var res = new List<Instalacion>();

        // 1) la instalacion activa
        var activa = Rim.Cfg.GamePath;
        var exeActiva = BuscarExe(activa);
        if (exeActiva != null)
            res.Add(new Instalacion(
                "ACTIVA — " + NombreCarpeta(activa), exeActiva, LeerVersion(activa), true));

        // 2) las copias de la carpeta de versiones
        var vf = Rim.Cfg.VersionsFolder;
        if (!string.IsNullOrWhiteSpace(vf) && Directory.Exists(vf))
        {
            foreach (var d in Directory.GetDirectories(vf))
            {
                // la copia puede tener el exe en la raiz o dentro de una subcarpeta
                var exe = BuscarExe(d);
                if (exe == null) continue;
                if (exeActiva != null &&
                    string.Equals(exe, exeActiva, StringComparison.OrdinalIgnoreCase)) continue;

                res.Add(new Instalacion(
                    NombreCarpeta(d), exe, LeerVersion(d), false));
            }
        }

        return res;
    }

    private static string NombreCarpeta(string ruta)
    {
        try { return new DirectoryInfo(ruta.TrimEnd('\\', '/')).Name; }
        catch { return ruta; }
    }

    /// <summary>Busca el ejecutable, en la raiz o un nivel mas abajo.</summary>
    public static string? BuscarExe(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return null;
        try
        {
            foreach (var n in new[] { "RimWorld.exe", "RimWorldWin64.exe" })
            {
                var p = Path.Combine(dir, n);
                if (File.Exists(p)) return p;
            }
            // copia que contiene una carpeta RimWorld dentro
            var anidada = Path.Combine(dir, "RimWorld");
            if (Directory.Exists(anidada))
                foreach (var n in new[] { "RimWorld.exe", "RimWorldWin64.exe" })
                {
                    var p = Path.Combine(anidada, n);
                    if (File.Exists(p)) return p;
                }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Version segun Version.txt. Se busca en la carpeta indicada y, si el juego
    /// esta en una subcarpeta RimWorld\, tambien un nivel mas abajo.
    /// </summary>
    public static string LeerVersion(string dir)
    {
        foreach (var carpeta in new[] { dir, Path.Combine(dir, "RimWorld") })
        {
            try
            {
                var f = Path.Combine(carpeta, "Version.txt");
                if (!File.Exists(f)) continue;
                var t = File.ReadAllText(f).Trim();
                if (t.Length == 0) continue;
                return t.Length > 40 ? t[..40] : t;
            }
            catch { }
        }
        return "";
    }

    /// <summary>
    /// Abre una instalacion concreta. El juego se lanza desde su carpeta.
    ///
    /// Si <paramref name="datosSeparados"/> es true se pasa -savedatafolder, para que
    /// esa version no comparta partidas ni ModsConfig con la activa: abrir una 1.6
    /// sobre los datos de la 1.7 puede corromper la configuracion de mods.
    /// </summary>
    public static bool Abrir(Instalacion inst, bool datosSeparados, out string error)
    {
        error = "";
        try
        {
            if (!File.Exists(inst.Exe)) { error = "No existe " + inst.Exe; return false; }

            var psi = new ProcessStartInfo
            {
                FileName = inst.Exe,
                WorkingDirectory = Path.GetDirectoryName(inst.Exe) ?? "",
                UseShellExecute = true,
            };

            if (datosSeparados && !inst.Activa)
            {
                var carpeta = Path.GetDirectoryName(inst.Exe) ?? "";
                var datos = Path.Combine(carpeta, "Datos");
                try { if (!Directory.Exists(datos)) Directory.CreateDirectory(datos); } catch { }
                // el juego espera barras normales en este argumento
                psi.Arguments = $"-savedatafolder=\"{datos.Replace('\\', '/')}\"";
            }

            Process.Start(psi);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    /// <summary>Carpeta de datos que usaria una version con datos separados.</summary>
    public static string CarpetaDatos(Instalacion inst)
        => Path.Combine(Path.GetDirectoryName(inst.Exe) ?? "", "Datos");
}
