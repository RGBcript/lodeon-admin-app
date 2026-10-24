using System.Drawing;

namespace LodeonAdmin;

/// <summary>
/// Ventana para elegir cual de las instalaciones encontradas abrir.
/// Aparece desde el boton «Versiones» de la pagina de estado.
/// </summary>
public class VersionesForm : Form
{
    private readonly List<Instalacion> _lista;

    public VersionesForm(List<Instalacion> lista)
    {
        _lista = lista;

        Text = "Versiones instaladas";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(660, 420);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body();

        var titulo = new Label
        {
            Text = "Elige qué versión abrir",
            Font = Theme.H1(),
            ForeColor = Theme.Text,
            BackColor = Theme.Bg,
            Bounds = new Rectangle(20, 16, 600, 30),
        };
        Controls.Add(titulo);

        var sub = new Label
        {
            Text = "Solo se pueden abrir las que tienen RimWorld.exe. La activa es la que usa Lodeon Admin para gestionar mods.",
            Font = Theme.Caption(),
            ForeColor = Theme.Text3,
            BackColor = Theme.Bg,
            Bounds = new Rectangle(20, 46, 620, 22),
        };
        Controls.Add(sub);

        var lv = new ListView
        {
            Bounds = new Rectangle(20, 76, 620, 210),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            Font = Theme.Body(),
            HideSelection = false,
        };
        lv.Columns.Add("Versión", 170);
        lv.Columns.Add("Carpeta", 250);
        lv.Columns.Add("Estado", 190);

        foreach (var i in _lista)
        {
            var it = new ListViewItem(string.IsNullOrWhiteSpace(i.Version) ? "(sin Version.txt)" : i.Version);
            it.SubItems.Add(i.Nombre);
            it.SubItems.Add(i.Activa ? "en uso" : "disponible");
            it.Tag = i;
            if (i.Activa) it.ForeColor = Theme.Green;
            lv.Items.Add(it);
        }
        if (lv.Items.Count > 0) lv.Items[0].Selected = true;
        Controls.Add(lv);

        var separados = new CheckBox
        {
            Text = "Usar datos separados (no comparte partidas ni ajustes)",
            Bounds = new Rectangle(20, 296, 460, 24),
            Checked = true,
            BackColor = Theme.Bg,
            ForeColor = Theme.Text2,
            // FlatStyle.Flat pinta la marca casi invisible sobre fondo oscuro:
            // se usa el estilo normal para que se distinga si está activada.
            FlatStyle = FlatStyle.Standard,
            Font = Theme.Caption(),
        };
        Controls.Add(separados);

        var abrir = new FlatBtn("Abrir esta", 0, 0, 140, 32) { Name = "btnAbrirVersion" };
        abrir.Base = Theme.Green;
        abrir.HoverColor = Color.FromArgb(112, 202, 148);
        abrir.TextColor = Color.FromArgb(10, 20, 14);
        abrir.Bounds = new Rectangle(20, 334, 140, 32);
        abrir.Click += (_, _) => AbrirSeleccionada(lv, separados.Checked);
        Controls.Add(abrir);

        var abrirCarpeta = new FlatBtn("Abrir carpeta", 0, 0, 140, 32);
        abrirCarpeta.Bounds = new Rectangle(172, 334, 140, 32);
        abrirCarpeta.Click += (_, _) =>
        {
            if (lv.SelectedItems.Count == 0) return;
            var i = (Instalacion)lv.SelectedItems[0].Tag!;
            var dir = Path.GetDirectoryName(i.Exe);
            if (dir != null && Directory.Exists(dir))
                try { System.Diagnostics.Process.Start("explorer.exe", dir); } catch { }
        };
        Controls.Add(abrirCarpeta);

        var cerrar = new FlatBtn("Cerrar", 0, 0, 110, 32);
        cerrar.Bounds = new Rectangle(530, 334, 110, 32);
        cerrar.Click += (_, _) => Close();
        Controls.Add(cerrar);

        lv.DoubleClick += (_, _) => AbrirSeleccionada(lv, separados.Checked);

        if (_lista.Count == 0)
        {
            titulo.Text = "No hay versiones instaladas";
            sub.Text = "Configura la carpeta de copias en Ajustes para que aparezcan aquí.";
            abrir.Enabled = false;
            abrirCarpeta.Enabled = false;
        }
    }

    private void AbrirSeleccionada(ListView lv, bool datosSeparados)
    {
        if (lv.SelectedItems.Count == 0) return;
        var inst = (Instalacion)lv.SelectedItems[0].Tag!;

        if (Rim.GameRunning())
        {
            MessageBox.Show(this, "RimWorld ya está en ejecución. Ciérralo primero.",
                "Versiones", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!Versiones.Abrir(inst, datosSeparados, out var err))
        {
            MessageBox.Show(this, "No se pudo abrir:\n" + err, "Versiones",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Close();
    }
}
