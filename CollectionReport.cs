using System.Text;

namespace LodeonAdmin;

/// <summary>
/// Prueba el resolvedor de colecciones del Workshop sin abrir la ventana.
///
/// Recibe uno o varios ids y muestra, para cada uno, si es un mod suelto o una
/// coleccion, y cuantos hijos tiene. Sirve para comprobar la función contra
/// colecciones reales.
/// </summary>
public static class CollectionReport
{
    public static int Run(string[] args)
    {
        var ids = args.Skip(1)
                      .Select(a => a.TrimStart('-', '/'))
                      .Where(a => System.Text.RegularExpressions.Regex.IsMatch(a, @"^\d{6,}$"))
                      .Distinct().ToList();

        var sb = new StringBuilder();
        sb.AppendLine("=== COLECCIONES Y MODS ===");
        sb.AppendLine();

        if (ids.Count == 0)
        {
            sb.AppendLine("Uso: LodeonAdmin.exe -coleccion <id> [<id> ...]");
            sb.AppendLine();
            sb.AppendLine("Prueba con una colección conocida, por ejemplo una lista de mods de RimWorld.");
            return Escribir(sb, false);
        }

        int colecciones = 0, mods = 0, errores = 0;

        foreach (var id in ids)
        {
            sb.AppendLine($"--- {id} ---");
            var (hijos, err) = Workshop.HijosDeColeccion(id);

            if (hijos.Count > 0)
            {
                colecciones++;
                sb.AppendLine($"  COLECCIÓN con {hijos.Count} mods:");
                foreach (var h in hijos.Take(15)) sb.AppendLine("    " + h);
                if (hijos.Count > 15) sb.AppendLine($"    ... y {hijos.Count - 15} más");
            }
            else
            {
                // no es coleccion: se comprueba si al menos existe como mod
                var (existe, detalle) = ComprobarMod(id);
                if (existe) { mods++; sb.AppendLine($"  MOD suelto — {detalle}"); }
                else { errores++; sb.AppendLine($"  ni colección ni mod ({err} / {detalle})"); }
            }
            sb.AppendLine();
        }

        sb.AppendLine($"Colecciones: {colecciones}   Mods sueltos: {mods}   Fallos: {errores}");
        bool ok = errores == 0;
        sb.AppendLine(ok ? "RESULTADO: OK" : "RESULTADO: hubo fallos");

        return Escribir(sb, ok);
    }

    /// <summary>Pregunta a la API si el id corresponde a un mod y devuelve su título.</summary>
    private static (bool existe, string detalle) ComprobarMod(string id)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string,string>("itemcount", "1"),
                new KeyValuePair<string,string>("publishedfileids[0]", id),
            });
            using var resp = http.PostAsync(
                "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/", body)
                .GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return (false, "HTTP " + (int)resp.StatusCode);

            var txt = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = System.Text.Json.JsonDocument.Parse(txt);
            if (!doc.RootElement.TryGetProperty("response", out var r)) return (false, "sin respuesta");
            if (!r.TryGetProperty("publishedfiledetails", out var d) || d.GetArrayLength() == 0)
                return (false, "sin detalles");

            var f = d[0];
            int result = f.TryGetProperty("result", out var rv) && rv.TryGetInt32(out var rc) ? rc : 0;
            if (result != 1) return (false, "resultado " + result);

            var titulo = f.TryGetProperty("title", out var t) ? (t.GetString() ?? "") : "";
            int subs = f.TryGetProperty("subscriptions", out var s) && s.TryGetInt32(out var si) ? si : 0;
            return (true, $"{titulo}  ({subs:N0} suscriptores)");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    private static int Escribir(StringBuilder sb, bool ok)
    {
        var txt = sb.ToString();
        try { File.WriteAllText(Path.Combine(AppConfig.ExeDir, "coleccion.txt"), txt, Encoding.UTF8); } catch { }
        try { Console.OutputEncoding = Encoding.UTF8; Console.Write(txt); } catch { }
        return ok ? 0 : 1;
    }
}
