using System.Diagnostics;
using System.Text;
namespace LodeonAdmin;

/// <summary>Mantenimiento de mods: copias, dependencias, enlaces y orden.</summary>
public partial class MainForm
{
    // ============================================================ mantenimiento

    private void LimpiarBackups()
    {
        var (mb, c) = Rim.BackupsInfo();
        if (c == 0)
        {
            MessageBox.Show(this, "No hay copias de seguridad.", "Lodeon Admin",
                MessageBoxButtons.OK, MessageBoxIcon.Information); return;
        }
        if (MessageBox.Show(this, $"Hay {c} carpetas de copia ({mb} MB).\n\n" +
                "Se borrarán las antiguas conservando la más reciente de cada mod.\n\n¿Continuar?",
                "Limpiar copias", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        int n = 0;
        try
        {
            foreach (var g in Directory.GetDirectories(Rim.VersionsDir)
                                        .GroupBy(d => Path.GetFileName(d).Split("__")[0]))
                foreach (var viejo in g.OrderByDescending(x => x).Skip(1))
                {
                    Directory.Delete(viejo, true);
                    n++;
                }
            var (mb2, c2) = Rim.BackupsInfo();
            MessageBox.Show(this, $"Borradas {n}. Quedan {c2} ({mb2} MB).", "Lodeon Admin",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Reordena la lista activa segun loadAfter/loadBefore y ofrece aplicarlo.
    /// Muestra primero que cambiaria, sin escribir nada.
    /// </summary>
    private void OrdenarMods()
    {
        try
        {
            var activos = Rim.ActiveMods();
            if (activos.Count == 0)
            {
                MessageBox.Show(this, "No hay mods activos.", "Ordenar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var r = Sorter.Ordenar(activos);

            // cambios respecto al orden actual
            var cambios = new List<string>();
            for (int i = 0; i < Math.Min(activos.Count, r.Orden.Count); i++)
                if (!string.Equals(activos[i], r.Orden[i], StringComparison.OrdinalIgnoreCase))
                { cambios.Add(i.ToString()); break; }

            int movidos = 0;
            var posActual = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < activos.Count; i++) posActual[activos[i]] = i;
            for (int i = 0; i < r.Orden.Count; i++)
            {
                if (posActual.TryGetValue(r.Orden[i], out var p) && p != i) movidos++;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"{activos.Count} mods activos · {movidos} cambiarían de posición");
            sb.AppendLine();
            if (r.Ciclos > 0) sb.AppendLine($"Aviso: {r.Ciclos} mods forman un ciclo de orden.");
            if (r.Avisos.Count > 0)
            {
                sb.AppendLine("Avisos:");
                foreach (var a in r.Avisos.Take(6)) sb.AppendLine("  · " + a);
                sb.AppendLine();
            }
            sb.AppendLine("Primeros 10 del orden propuesto:");
            foreach (var x in r.Orden.Take(10)) sb.AppendLine("  " + x);

            if (movidos == 0)
            {
                MessageBox.Show(this, sb + "\n\nLa lista ya está bien ordenada.",
                    "Ordenar", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var resp = MessageBox.Show(this,
                sb + "\n\n¿Aplicar este orden? Se guarda una copia antes de escribir.",
                "Ordenar por reglas", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (resp != DialogResult.Yes) return;

            if (!Rim.SetActiveMods(r.Orden, out var bak, out var err))
            {
                MessageBox.Show(this, "No se pudo escribir: " + err, "Ordenar",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            MessageBox.Show(this,
                $"Orden aplicado: {movidos} mods movidos.\n\nCopia de seguridad:\n{Path.GetFileName(bak)}\n\nReinicia el juego para cargar el nuevo orden.",
                "Ordenar", MessageBoxButtons.OK, MessageBoxIcon.Information);

            CargarLista();
            RefreshAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Error al ordenar: " + ex.Message, "Ordenar",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Mods activos que se declaran incompatibles entre sí.</summary>
    private void ComprobarIncompatibles()
    {
        try
        {
            var activos = Rim.ActiveMods();
            var inc = Sorter.Incompatibilidades(activos);

            if (inc.Count == 0)
            {
                MessageBox.Show(this,
                    $"Ninguno de los {activos.Count} mods activos declara incompatibilidad con otro que tengas activo.",
                    "Incompatibilidades", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            MessageBox.Show(this,
                $"{inc.Count} incompatibilidades declaradas:\n\n" + string.Join("\n", inc.Take(20)) +
                (inc.Count > 20 ? $"\n… y {inc.Count - 20} más" : ""),
                "Incompatibilidades", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Error: " + ex.Message, "Incompatibilidades",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ComprobarDeps()
    {
        try
        {
            var mods = Rim.AllMods();
            var porId = mods.ToDictionary(m => m.PackageId.ToLowerInvariant(), m => m);
            var activos = new HashSet<string>(Rim.ActiveMods().Select(x => x.ToLowerInvariant()));
            var probs = new List<string>();

            foreach (var m in mods.Where(x => x.Active))
            {
                var about = Path.Combine(Rim.ModsDir, m.Folder, "About", "About.xml");
                if (!File.Exists(about)) continue;
                var doc = System.Xml.Linq.XDocument.Load(about);
                var deps = doc.Root?.Element("modDependencies");
                if (deps == null) continue;
                foreach (var li in deps.Elements("li"))
                {
                    var dep = (li.Element("packageId")?.Value ?? "").Trim().ToLowerInvariant();
                    if (dep.Length == 0 || dep.StartsWith("ludeon.")) continue;
                    if (!porId.ContainsKey(dep)) probs.Add($"{m.Name}  necesita  {dep}  (no instalado)");
                    else if (!activos.Contains(dep)) probs.Add($"{m.Name}  necesita  {dep}  (desactivado)");
                }
            }

            if (probs.Count == 0)
                MessageBox.Show(this, "Todas las dependencias están cubiertas.", "Lodeon Admin",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show(this, $"{probs.Count} problemas encontrados:\n\n" + string.Join("\n", probs.Take(20)),
                    "Dependencias", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ============================================================ workshop
}
