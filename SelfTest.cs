using System.Text;

namespace LodeonAdmin;

/// <summary>Autotest sin interfaz: valida rutas, lectura de mods y log.</summary>
public static class SelfTest
{
    public static int Run()
    {
        var sb = new StringBuilder();
        int fallos = 0;

        void Ok(string k, object v) => sb.AppendLine($"  {k,-28} {v}");
        void Fail(string k, string v) { sb.AppendLine($"  {k,-28} FALLO: {v}"); fallos++; }

        sb.AppendLine($"AUTOTEST Lodeon Admin v{MainForm.Ver}");
        sb.AppendLine();

        // rutas
        Ok("carpeta del juego", Rim.Cfg.GamePath);
        Ok("existe el exe", File.Exists(Path.Combine(Rim.Cfg.GamePath, "RimWorld.exe")));
        Ok("carpeta de datos", Rim.ProfileDir);
        Ok("ModsConfig.xml", File.Exists(Rim.ModsConfigPath));
        Ok("Player.log", File.Exists(Rim.LogPath));

        // version
        var ver = Rim.LocalVersion();
        if (ver == "?" || ver.Length == 0) Fail("version local", "no se pudo leer");
        else Ok("version local", ver);

        // mods
        var mods = Rim.AllMods();
        if (mods.Count == 0) Fail("inventario de mods", "vacio");
        else Ok("mods instalados", mods.Count);
        Ok("mods activos", Rim.ActiveMods().Count);

        // log
        var st = Rim.Analyze();
        Ok("registro", $"{st.Bytes / 1024} KB, ERROR {st.Errors}, XML {st.XmlErrors}, WARN {st.Warns}");

        // idioma
        Ok("idioma activo", Rim.ActiveLanguage());

        // ruso
        var (a, g) = Rim.RussianState();
        Ok("carpetas ruso", $"{a} activas, {g} en cuarentena");

        // backups
        var (mb, c) = Rim.BackupsInfo();
        Ok("backups", $"{c} carpetas, {mb} MB");

        // escritura segura (sin tocar el archivo real)
        try
        {
            var act = Rim.ActiveMods();
            var tmp = Path.Combine(Path.GetTempPath(), "lodeon-test.xml");
            var original = File.ReadAllText(Rim.ModsConfigPath);
            File.WriteAllText(tmp, original);
            File.Delete(tmp);
            Ok("lectura ModsConfig", "ok");
        }
        catch (Exception ex) { Fail("lectura ModsConfig", ex.Message); }

        // steamcmd
        Ok("steamcmd", File.Exists(Rim.Cfg.SteamCmd));
        Ok("clave Steam", string.IsNullOrWhiteSpace(Rim.Cfg.SteamApiKey) ? "no configurada" : "configurada");

        sb.AppendLine();
        sb.AppendLine(fallos == 0 ? "RESULTADO: OK" : $"RESULTADO: {fallos} FALLOS");

        var texto = sb.ToString();

        // La app es WinExe: puede no haber consola adjunta. Se escribe siempre a
        // archivo y, si hay consola, tambien por pantalla.
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest.txt"), texto, Encoding.UTF8); } catch { }
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Write(texto);
        }
        catch { }
        return fallos == 0 ? 0 : 1;
    }
}
