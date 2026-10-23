using System.Diagnostics;

namespace LodeonAdmin;

/// <summary>
/// Pagina de ajustes: rutas de la instalacion, y prueba de la clave de Steam.
/// Sin esto, el programa solo funcionaba en la maquina donde se compilo.
/// </summary>
public partial class MainForm
{
    private readonly TextBox _txtGamePath = new();
    private readonly TextBox _txtSteamCmdPath = new();
    private readonly TextBox _txtVersiones = new();
    private readonly Label _lblEstadoRutas = new();

    private void BuildAjustes()
    {
        var p = NewPage("ajustes");
        p.Controls.Add(Head("Ajustes",
            "Dónde está el juego y las herramientas. Si algo no se detectó, indícalo aquí."));

        void Etiqueta(string texto, int y)
        {
            var l = new Label
            {
                Text = texto,
                BackColor = Theme.Bg,
                ForeColor = Theme.Text3,
                Font = Theme.Caption(),
            };
            l.Name = "lbl_" + y;
            l.Tag = y;
            p.Controls.Add(l);
        }

        Etiqueta("Carpeta del juego", 86);
        PrepararCampo(_txtGamePath, "txtGamePath");
        p.Controls.Add(_txtGamePath);

        var examinarJuego = new FlatBtn("Examinar", 0, 0, 100, 30) { Name = "btnExamJuego" };
        examinarJuego.Click += (_, _) => ElegirCarpeta(_txtGamePath, "Carpeta de RimWorld");
        p.Controls.Add(examinarJuego);

        Etiqueta("SteamCMD (solo para descargar del Workshop)", 170);
        PrepararCampo(_txtSteamCmdPath, "txtSteamCmd");
        p.Controls.Add(_txtSteamCmdPath);

        var examinarCmd = new FlatBtn("Examinar", 0, 0, 100, 30) { Name = "btnExamCmd" };
        examinarCmd.Click += (_, _) => ElegirArchivo(_txtSteamCmdPath, "steamcmd.exe");
        p.Controls.Add(examinarCmd);

        Etiqueta("Carpeta para copias de versiones", 254);
        PrepararCampo(_txtVersiones, "txtVersiones");
        p.Controls.Add(_txtVersiones);

        var examinarVer = new FlatBtn("Examinar", 0, 0, 100, 30) { Name = "btnExamVer" };
        examinarVer.Click += (_, _) => ElegirCarpeta(_txtVersiones, "Carpeta de copias");
        p.Controls.Add(examinarVer);

        _lblEstadoRutas.Name = "lblEstadoRutas";
        _lblEstadoRutas.BackColor = Theme.Bg;
        _lblEstadoRutas.ForeColor = Theme.Text2;
        _lblEstadoRutas.Font = Theme.Caption();
        p.Controls.Add(_lblEstadoRutas);

        var detectar = new FlatBtn("Detectar automáticamente", 0, 0, 220, 32) { Name = "btnDetectar" };
        detectar.Click += (_, _) => DetectarRutas();
        p.Controls.Add(detectar);

        var guardar = new FlatBtn("Guardar", 0, 0, 130, 32) { Name = "btnGuardarRutas" };
        guardar.Base = Theme.Blue;
        guardar.HoverColor = Color.FromArgb(116, 168, 218);
        guardar.TextColor = Color.White;
        guardar.Click += (_, _) => GuardarRutas();
        p.Controls.Add(guardar);

        var abrirCfg = new FlatBtn("Abrir carpeta", 0, 0, 140, 32) { Name = "btnAbrirCfg" };
        abrirCfg.Click += (_, _) => OpenPath(AppConfig.ExeDir);
        p.Controls.Add(abrirCfg);
    }

    private static void PrepararCampo(TextBox t, string nombre)
    {
        t.Name = nombre;
        t.BackColor = Theme.Card;
        t.ForeColor = Theme.Text;
        t.BorderStyle = BorderStyle.FixedSingle;
        t.Font = Theme.Body();
    }

    /// <summary>
    /// Si no hay una carpeta de juego válida, no tiene sentido enseñar seis páginas
    /// vacías: se manda al usuario a Ajustes con el motivo explicado.
    /// </summary>
    private void AvisarRutasSiFaltaJuego()
    {
        // si la deteccion arreglo una ruta vieja, se avisa aunque ya sea valida
        if (AppConfig.RutasCambiadas.Length > 0)
        {
            MessageBox.Show(this,
                AppConfig.RutasCambiadas + "\nSe actualizó la ruta automáticamente.",
                "Ruta actualizada", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        if (GameFinder.EsJuego(Rim.Cfg.GamePath)) return;

        Select("ajustes");

        MessageBox.Show(this,
            "No encuentro la instalación de RimWorld.\n\n" +
            (string.IsNullOrWhiteSpace(Rim.Cfg.GamePath)
                ? "No hay ninguna ruta configurada."
                : "La ruta actual no es válida:\n" + Rim.Cfg.GamePath) + "\n\n" +
            "Pulsa «Detectar automáticamente» o elige la carpeta con «Examinar».",
            "Falta la carpeta del juego", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    /// <summary>Vuelca la configuracion actual en los campos.</summary>
    private void CargarAjustes()
    {
        var c = Rim.Cfg;
        _txtGamePath.Text = c.GamePath;
        _txtSteamCmdPath.Text = c.SteamCmd;
        _txtVersiones.Text = c.VersionsFolder;
        PintarEstadoRutas();
    }

    private void PintarEstadoRutas()
    {
        bool juego = GameFinder.EsJuego(_txtGamePath.Text.Trim());
        bool cmd = File.Exists(_txtSteamCmdPath.Text.Trim());

        var sb = new System.Text.StringBuilder();
        sb.Append(juego
            ? "Juego: encontrado y válido."
            : "Juego: la carpeta no parece una instalación de RimWorld (falta RimWorld.exe).");
        sb.AppendLine();
        sb.Append(cmd
            ? "SteamCMD: encontrado."
            : "SteamCMD: no encontrado. Las descargas del Workshop no funcionarán.");

        _lblEstadoRutas.Text = sb.ToString();
        _lblEstadoRutas.ForeColor = juego ? Theme.Text2 : Theme.Red;
    }

    private void DetectarRutas()
    {
        var juego = GameFinder.BuscarJuego();
        var cmd = GameFinder.BuscarSteamCmd();

        if (juego == null && cmd == null)
        {
            MessageBox.Show(this,
                "No se encontró ni RimWorld ni SteamCMD.\n\n" +
                "Indica la carpeta del juego a mano con «Examinar».",
                "Detección", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (juego != null) _txtGamePath.Text = juego;
        if (cmd != null) _txtSteamCmdPath.Text = cmd;
        PintarEstadoRutas();

        var msg = (juego != null ? "Juego: " + juego + "\n\n" : "Juego: no encontrado\n\n") +
                  (cmd != null ? "SteamCMD: " + cmd : "SteamCMD: no encontrado");
        MessageBox.Show(this, msg + "\n\nPulsa «Guardar» para aplicar.", "Detección",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void GuardarRutas()
    {
        var juego = _txtGamePath.Text.Trim();
        if (!GameFinder.EsJuego(juego))
        {
            var r = MessageBox.Show(this,
                "Esa carpeta no parece contener RimWorld (no encuentro RimWorld.exe).\n\n" +
                "¿Guardar de todas formas?",
                "Carpeta dudosa", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (r != DialogResult.Yes) return;
        }

        Rim.Cfg.GamePath = juego;
        Rim.Cfg.SteamCmd = _txtSteamCmdPath.Text.Trim();
        Rim.Cfg.VersionsFolder = _txtVersiones.Text.Trim();
        Rim.Cfg.RutasManuales = true;      // que no se sobrescriba sola al arrancar
        Rim.Cfg.Save();

        PintarEstadoRutas();
        RefreshAll();

        MessageBox.Show(this, "Guardado. La configuración se aplicó al momento.",
            "Ajustes", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Selector de carpeta. Sin FolderBrowserDialog cutre: usa el moderno del sistema.</summary>
    private void ElegirCarpeta(TextBox destino, string titulo)
    {
        try
        {
            using var dlg = new FolderBrowserDialog
            {
                Description = titulo,
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
                SelectedPath = Directory.Exists(destino.Text.Trim()) ? destino.Text.Trim() : "",
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                destino.Text = dlg.SelectedPath;
                PintarEstadoRutas();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ElegirArchivo(TextBox destino, string filtroNombre)
    {
        try
        {
            using var dlg = new OpenFileDialog { Filter = $"{filtroNombre}|{filtroNombre}|Todos|*.*" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                destino.Text = dlg.FileName;
                PintarEstadoRutas();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
