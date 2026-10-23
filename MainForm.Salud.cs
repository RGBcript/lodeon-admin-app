using System.Diagnostics;
using System.Text;
namespace LodeonAdmin;

/// <summary>Pagina de salud: informe unico y desactivacion del mod senalado.</summary>
public partial class MainForm
{
    // ============================================================ SALUD

    private readonly ListView _lvSalud = new();
    private readonly Panel _veredicto = new();
    private Health.Informe? _informe;

    private void BuildSalud()
    {
        var p = NewPage("salud");
        p.Controls.Add(Head("Salud", "Revisa el registro, las dependencias y el orden en un solo sitio."));

        _veredicto.Name = "veredicto";
        _veredicto.BackColor = Theme.Bg;
        p.Controls.Add(_veredicto);

        _lvSalud.Name = "lvSalud";
        _lvSalud.View = View.Details;
        _lvSalud.FullRowSelect = true;
        _lvSalud.MultiSelect = false;
        _lvSalud.BackColor = Theme.Card;
        _lvSalud.ForeColor = Theme.Text;
        _lvSalud.BorderStyle = BorderStyle.None;
        _lvSalud.Font = Theme.Caption();
        _lvSalud.Columns.Add("Tipo", 150);
        _lvSalud.Columns.Add("Mod", 260);
        _lvSalud.Columns.Add("Detalle", 560);
        p.Controls.Add(_lvSalud);
        DarkScroll.Apply(_lvSalud);

        var bar = new Panel { Name = "barraSalud", BackColor = Theme.Bg };
        p.Controls.Add(bar);

        var recalc = new FlatBtn("Recalcular", 0, 0, 120, 32);
        recalc.Base = Theme.Blue;
        recalc.HoverColor = Color.FromArgb(116, 168, 218);
        recalc.TextColor = Color.White;
        recalc.Click += (_, _) => CargarSalud();
        bar.Controls.Add(recalc);

        var desac = new FlatBtn("Desactivar seleccionado", 128, 0, 200, 32);
        desac.Click += (_, _) => DesactivarDesdeSalud();
        bar.Controls.Add(desac);

        var log = new FlatBtn("Abrir registro", 336, 0, 140, 32);
        log.Click += (_, _) => OpenPath(Rim.LogPath);
        bar.Controls.Add(log);
    }

    /// <summary>Recalcula el informe y lo pinta.</summary>
    private void CargarSalud()
    {
        try
        {
            _lvSalud.BeginUpdate();
            _lvSalud.Items.Clear();

            _informe = Health.Generar();

            void Fila(string tipo, string mod, string detalle, Color color, string? tag = null)
            {
                var it = new ListViewItem(tipo) { ForeColor = color, Tag = tag ?? "" };
                it.SubItems.Add(mod);
                it.SubItems.Add(detalle);
                _lvSalud.Items.Add(it);
            }

            foreach (var c in _informe.Culpables)
                Fila("Registro", c.Mod,
                     $"{c.Entradas} entrada(s), {c.Marcos} marco(s)" +
                     (c.Ejemplo.Length > 0 ? "  ·  " + Rec(c.Ejemplo, 90) : ""),
                     Theme.Gold, c.Carpeta);

            foreach (var s in _informe.DepsRotas)
            {
                var partes = s.Split("  necesita  ", 2);
                Fila("Dependencia rota", partes[0], partes.Length > 1 ? "necesita " + partes[1] : s, Theme.Red);
            }

            foreach (var s in _informe.Incompatibles)
                Fila("Incompatible", s, "", Theme.Red);

            foreach (var s in _informe.OrdenRoto)
                Fila("Orden", "", s, Theme.Gold);

            // Solo interesa el mod que declara versiones viejas Y ademas aparece en el
            // registro: ahi la falta de soporte explica algo. Los otros ~250 no aportan.
            var enLog = new HashSet<string>(_informe.Culpables.Select(c => c.Carpeta),
                                            StringComparer.OrdinalIgnoreCase);
            foreach (var s in _informe.SinSoporte17)
            {
                var partes = s.Split("   [", 2);
                var nombre = partes[0];
                bool coincide = enLog.Any(c =>
                    c.Equals(nombre, StringComparison.OrdinalIgnoreCase) ||
                    c.Contains(nombre, StringComparison.OrdinalIgnoreCase) ||
                    nombre.Contains(c, StringComparison.OrdinalIgnoreCase));
                if (!coincide) continue;
                Fila("Sin 1.7 y con avisos", nombre,
                     partes.Length > 1 ? "declara [" + partes[1] : "", Theme.Gold);
            }

            _lvSalud.EndUpdate();
            PintarVeredicto();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Error al calcular: " + ex.Message, "Salud",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void PintarVeredicto()
    {
        _veredicto.Invalidate();
        var inf = _informe;
        _veredicto.Paint += (s, e) =>
        {
            var g = e.Graphics;
            using (var bg = new SolidBrush(Theme.Bg))
                g.FillRectangle(bg, _veredicto.ClientRectangle);

            if (inf == null) return;
            var color = inf.Total == 0 && inf.ErroresLog == 0 ? Theme.Green
                      : inf.ErroresLog > 0 ? Theme.Red : Theme.Gold;

            var txt = inf.Sano
                ? "Sin problemas detectados."
                : inf.ErroresLog > 0
                    ? $"{inf.ErroresLog} error(es) en el registro · {inf.Total} otro(s) problema(s)"
                    : $"{inf.Total} problema(s) detectado(s)";

            Theme.Text1(g, txt, Theme.H2(), new Rectangle(0, 0, _veredicto.Width, 22), color);
            Theme.Text1(g,
                $"Registro: {inf.AvisosLog} aviso(s) · mods mencionados: {inf.Culpables.Count} · " +
                $"dependencias: {inf.DepsRotas.Count} · incompatibles: {inf.Incompatibles.Count} · " +
                $"orden: {inf.OrdenRoto.Count}",
                Theme.Caption(), new Rectangle(0, 22, _veredicto.Width, 16), Theme.Text3);
        };
    }

    /// <summary>Desactiva el mod de la fila seleccionada.</summary>
    private void DesactivarDesdeSalud()
    {
        if (_lvSalud.SelectedItems.Count == 0)
        {
            MessageBox.Show(this, "Elige una fila de la lista.", "Salud",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var fila = _lvSalud.SelectedItems[0];
        var carpeta = fila.Tag as string ?? "";
        if (carpeta.Length == 0)
        {
            MessageBox.Show(this, "Esa fila no corresponde a un mod concreto.", "Salud",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var mod = Rim.AllMods().FirstOrDefault(m =>
            string.Equals(m.Folder, carpeta, StringComparison.OrdinalIgnoreCase));
        if (mod == null || mod.PackageId.Length == 0)
        {
            MessageBox.Show(this, "No encontré el packageId de esa carpeta.", "Salud",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(this,
            $"Se desactivará «{mod.Name}».\n\nSe guarda copia de ModsConfig antes.\n\n¿Continuar?",
            "Desactivar mod", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        var activos = Rim.ActiveMods()
                         .Where(x => !string.Equals(x, mod.PackageId, StringComparison.OrdinalIgnoreCase))
                         .ToList();

        if (!Rim.SetActiveMods(activos, out var bak, out var err))
        {
            MessageBox.Show(this, "No se pudo escribir: " + err, "Salud",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        MessageBox.Show(this,
            $"«{mod.Name}» desactivado.\n\nCopia: {Path.GetFileName(bak)}\n\n" +
            (Rim.GameRunning() ? "Reinicia RimWorld para aplicarlo." : "Se aplicará al abrir el juego."),
            "Salud", MessageBoxButtons.OK, MessageBoxIcon.Information);

        CargarSalud();
        RefreshAll();
    }

    // ============================================================ WORKSHOP
}
