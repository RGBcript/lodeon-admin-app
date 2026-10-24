using System.Text;

namespace LodeonAdmin;

/// <summary>Lista las instalaciones de RimWorld encontradas, sin abrir la ventana.</summary>
public static class VersionsReport
{
    public static int Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== VERSIONES INSTALADAS ===");
        sb.AppendLine();
        sb.AppendLine($"Carpeta de copias : {Rim.Cfg.VersionsFolder}");
        sb.AppendLine($"Existe            : {Directory.Exists(Rim.Cfg.VersionsFolder)}");
        sb.AppendLine($"Instalación activa: {Rim.Cfg.GamePath}");
        sb.AppendLine();

        var lista = Versiones.Listar();
        if (lista.Count == 0)
        {
            sb.AppendLine("No se encontró ninguna instalación.");
        }
        else
        {
            sb.AppendLine($"Encontradas: {lista.Count}");
            sb.AppendLine();
            foreach (var i in lista)
            {
                sb.AppendLine($"  {(i.Activa ? "[activa] " : "         ")}{i.Nombre}");
                sb.AppendLine($"      versión : {(string.IsNullOrWhiteSpace(i.Version) ? "(sin Version.txt)" : i.Version)}");
                sb.AppendLine($"      exe     : {i.Exe}");
                if (!i.Activa)
                    sb.AppendLine($"      datos   : {Versiones.CarpetaDatos(i)}");
            }
        }
        sb.AppendLine();

        // una copia sin exe no se lista: se avisa de cuantas se descartaron
        var vf = Rim.Cfg.VersionsFolder;
        int descartadas = 0;
        if (!string.IsNullOrWhiteSpace(vf) && Directory.Exists(vf))
            foreach (var d in Directory.GetDirectories(vf))
                if (Versiones.BuscarExe(d) == null) descartadas++;
        if (descartadas > 0)
            sb.AppendLine($"Carpetas ignoradas por no tener RimWorld.exe: {descartadas}");

        bool activaOk = Versiones.BuscarExe(Rim.Cfg.GamePath) != null;
        sb.AppendLine(activaOk ? "RESULTADO: OK" : "RESULTADO: la instalación activa no es válida");

        var txt = sb.ToString();
        try { File.WriteAllText(Path.Combine(AppConfig.ExeDir, "versiones.txt"), txt, Encoding.UTF8); } catch { }
        try { Console.OutputEncoding = Encoding.UTF8; Console.Write(txt); } catch { }
        return activaOk ? 0 : 1;
    }
}
