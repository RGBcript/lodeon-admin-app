using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;

namespace LodeonAdmin;

public partial class MainForm : Form
{
    // navegacion
    private readonly Panel _side = new();
    private readonly Panel _pages = new();
    private readonly List<NavBtn> _navs = new();
    private readonly List<string> _keys = new() { "inicio", "salud", "workshop", "mods", "lista", "idioma", "ajustes" };

    // inicio
    private readonly StatCard _cVersion = new();
    private readonly StatCard _cMods = new();
    private readonly StatCard _cLog = new();
    private readonly StatCard _cIdioma = new();
    private readonly TextBox _txtLog = new();

    // lista
    private readonly ListView _lvMods = new();
    private readonly TextBox _txtFiltro = new();
    private readonly Label _lblLista = new();
    private readonly Dictionary<string, bool> _orig = new();
    private readonly List<Rim.ModInfo> _all = new();
    private List<Rim.ModInfo> _filtrados = new();
    private int _pagina;
    private const int PorPagina = 100;

    // idioma
    private readonly ListView _lvLangs = new();
    private readonly Label _lblLangDet = new();
    private readonly Panel _bridgeBox = new();

    // workshop
    private readonly TextBox _txtUrl = new();
    private readonly TextBox _txtOut = new();
    private readonly Label _lblKeyState = new();
    private readonly ListView _lvRes = new();

    private bool _busy;
    public string? StartPage { get; set; }
    public static string Ver => "1.8";

    public MainForm()
    {
        Text = "Lodeon Admin";
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(1020, 680);
        MinimumSize = new Size(860, 580);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.Body();
        DoubleBuffered = true;
        Icon = LoadIcon();

        BuildSidebar();
        BuildPages();

        Load += (_, _) =>
        {
            LayoutPages();
            RefreshAll();
            Select(StartPage ?? "inicio");
            SembrarOrdenSiFalta();
            AvisarRutasSiFaltaJuego();
            ComprobarModsConfig();
            DetectarIdiomaAlArrancar();
            AvisarActualizacionesAlArrancar();
        };
        Resize += (_, _) => LayoutPages();

        var t = new System.Windows.Forms.Timer { Interval = 15000 };
        t.Tick += (_, _) => { if (!_busy) RefreshLogOnly(); };
        t.Start();
    }

    private static Icon? LoadIcon()
    {
        try
        {
            var p = Path.Combine(AppConfig.ExeDir, "assets", "lodeon-v4.ico");
            if (File.Exists(p)) return new Icon(p);
        }
        catch { }
        return null;
    }

    /// <summary>
    /// La primera vez no hay indice de orden: se toma la lista actual como
    /// referencia. Sin esto, el primer cambio de activacion no tendria posiciones
    /// que respetar y todo se iria al final.
    /// </summary>
    private void SembrarOrdenSiFalta()
    {
        try
        {
            if (File.Exists(Order.FilePath)) return;
            var act = Rim.ActiveMods();
            if (act.Count == 0) return;
            Order.Guardar(act);
        }
        catch { }
    }

    // ============================================================ estructura

    private void BuildSidebar()
    {
        _side.Dock = DockStyle.Left;
        _side.Width = 178;
        _side.BackColor = Theme.Side;
        Controls.Add(_side);

        _side.Controls.Add(new Label
        {
            Text = "LODEON",
            Bounds = new Rectangle(22, 24, 140, 20),
            Font = Theme.H2(),
            ForeColor = Theme.Text,
            BackColor = Theme.Side,
        });
        _side.Controls.Add(new Label
        {
            Text = "administrador",
            Bounds = new Rectangle(22, 44, 140, 16),
            Font = Theme.Caption(),
            ForeColor = Theme.Text3,
            BackColor = Theme.Side,
        });

        var host = new Panel { Dock = DockStyle.Top, Height = 84 + 36 * 7, BackColor = Theme.Side };
        _side.Controls.Add(host);

        string[] textos = { "Inicio", "Salud", "Workshop", "Mods", "Lista", "Idioma", "Ajustes" };
        for (int i = 0; i < textos.Length; i++)
        {
            var nb = new NavBtn(textos[i], 0, 84 + i * 36, 178, 36);
            var k = _keys[i];
            nb.Click += (_, _) => Select(k);
            host.Controls.Add(nb);
            _navs.Add(nb);
        }

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 78, BackColor = Theme.Side };
        _side.Controls.Add(bottom);

        var play = new FlatBtn("JUGAR", 18, 18, 142, 40);
        play.Base = Theme.Green;
        play.HoverColor = Color.FromArgb(112, 202, 148);
        play.TextColor = Color.FromArgb(10, 20, 14);
        play.Font = Theme.BodyStrong();
        play.Click += (_, _) => PlayGame();
        bottom.Controls.Add(play);

        bottom.Controls.Add(new Label
        {
            Text = "v" + Ver,
            Bounds = new Rectangle(18, 60, 142, 14),
            Font = Theme.Caption(),
            ForeColor = Theme.Text3,
            BackColor = Theme.Side,
        });
    }

    private void BuildPages()
    {
        _pages.Dock = DockStyle.Fill;
        _pages.BackColor = Theme.Bg;
        Controls.Add(_pages);
        _pages.BringToFront();

        BuildInicio();
        BuildSalud();
        BuildWorkshop();
        BuildMods();
        BuildLista();
        BuildIdioma();
        BuildAjustes();

        Select("inicio");
    }

    private void Select(string key)
    {
        if (!_keys.Contains(key)) key = "inicio";

        var pg = _pages.Controls.Find("pg_" + key, false).FirstOrDefault();
        foreach (Control c in _pages.Controls) c.Visible = ReferenceEquals(c, pg);
        if (pg != null) { pg.Visible = true; pg.BringToFront(); }

        for (int i = 0; i < _navs.Count; i++)
        {
            _navs[i].Selected = _keys[i] == key;
            _navs[i].Invalidate();
        }

        if (key == "salud") CargarSalud();
        if (key == "ajustes") CargarAjustes();
        if (key == "lista") CargarLista();
        if (key == "idioma") RefrescarIdioma();
        if (key == "workshop") { ActualizarEstadoClave(); ActualizarBotonAviso(); }
        LayoutPages();
    }

    private Panel NewPage(string key)
    {
        var p = new Panel
        {
            Name = "pg_" + key,
            Dock = DockStyle.Fill,
            BackColor = Theme.Bg,
            Visible = false,
            AutoScroll = key != "inicio",
        };
        _pages.Controls.Add(p);
        return p;
    }

    private static PageHeader Head(string h, string d) =>
        new(h, d) { Bounds = new Rectangle(28, 24, 700, 42) };

    private static SectionLabel Section(string t) =>
        new(t) { Bounds = new Rectangle(28, 0, 400, 12) };

    private void LayoutPages()
    {
        foreach (Control pg in _pages.Controls)
        {
            int w = pg.ClientSize.Width;
            int h = pg.ClientSize.Height;
            int inner = Math.Max(320, w - 56);

            foreach (Control c in pg.Controls)
            {
                switch (c.Name)
                {
                    case "cards":
                        c.SetBounds(28, 82, inner, 84); break;
                    case "secLog":
                        c.SetBounds(28, 188, 300, 12); break;
                    case "logBox":
                        c.SetBounds(28, 206, inner, Math.Max(110, h - 206 - 66)); break;
                    case "barraInicio":
                        c.SetBounds(28, Math.Max(220, h - 50), inner, 32); break;

                    case "lvMods":
                        c.SetBounds(28, 148, inner, Math.Max(140, h - 148 - 62)); break;
                    case "filtro":
                        c.SetBounds(28, 82, Math.Max(180, Math.Min(320, inner / 3)), 26); break;
                    case "lblLista":
                    {
                        // el contador va a la derecha; se calcula para que no se salga
                        // de la ventana al estrecharla
                        int anchoFiltro = Math.Max(180, Math.Min(320, inner / 3));
                        int x = 28 + anchoFiltro + 16;
                        int ancho = Math.Max(160, inner - anchoFiltro - 16);
                        c.SetBounds(x, 86, ancho, 18);
                        break;
                    }
                    case "barraLista":
                        c.SetBounds(28, Math.Max(220, h - 48), inner, 32); break;

                    case "lblOcultos":
                    {
                        int altoLista = Math.Max(140, h - 166 - 240);
                        c.SetBounds(28, 166 + altoLista + 8,
                                    Math.Max(240, (int)(inner * 0.5)), 14);
                        break;
                    }
                    case "lvLangs":
                        c.SetBounds(28, 166, Math.Max(240, (int)(inner * 0.5)), Math.Max(140, h - 166 - 240)); break;
                    case "btnUsarIdioma":
                    {
                        int altoLista = Math.Max(140, h - 166 - 240);
                        c.SetBounds(28, 166 + altoLista + 28, 125, 30);
                        break;
                    }
                    case "btnAutoIdioma":
                    {
                        int altoLista = Math.Max(140, h - 166 - 240);
                        c.SetBounds(161, 166 + altoLista + 28, 130, 30);
                        break;
                    }
                    case "bridgeBox":
                        c.SetBounds(Math.Max(280, (int)(inner * 0.5) + 56), 166, Math.Max(200, inner - (int)(inner * 0.5) - 56), 250); break;
                    case "rusoBox":
                        c.SetBounds(28, Math.Max(180, h - 150), inner, 110); break;

                    case "txtUrl":
                        c.SetBounds(28, 82, Math.Max(160, inner - 160), 26); break;
                    case "btnBuscar":
                        c.SetBounds(Math.Max(210, inner - 132), 81, 132, 28); break;
                    case "lblKeyState":
                        c.SetBounds(28, 118, 480, 16); break;
                    case "txtKey":
                        c.SetBounds(28, 138, Math.Max(160, inner - 498), 24); break;
                    case "btnGuardarClave":
                        c.SetBounds(inner - 462, 137, 128, 26); break;
                    case "btnProbar":
                        c.SetBounds(inner - 326, 137, 132, 26); break;
                    case "btnDescargar":
                        // pegado al borde derecho: antes usaba un x fijo y se salia
                        c.SetBounds(inner - 186, 137, 186, 26); break;
                    case "btnActualizaciones":
                        c.SetBounds(28, 180, 220, 26); break;
                    case "btnVincular":
                        c.SetBounds(Math.Max(256, inner - 200), 180, 200, 26); break;
                    case "btnAviso":
                        c.SetBounds(256, 180, Math.Max(120, inner - 470), 26); break;
                    case "lvRes":
                        c.SetBounds(28, 216, inner, 140); break;
                    case "txtOut":
                        c.SetBounds(28, 368, inner, Math.Max(80, h - 368 - 28)); break;

                    case "veredicto":
                        c.SetBounds(28, 82, inner, 40); break;
                    case "lvSalud":
                        c.SetBounds(28, 132, inner, Math.Max(120, h - 132 - 62)); break;
                    case "barraSalud":
                        c.SetBounds(28, Math.Max(200, h - 48), inner, 32); break;
                    case string s when s.StartsWith("lbl_"):
                    {
                        if (c.Tag is int y) c.SetBounds(28, y, Math.Max(200, inner), 16);
                        break;
                    }
                    case "txtGamePath":
                        c.SetBounds(28, 104, Math.Max(300, inner - 130), 26); break;
                    case "btnExamJuego":
                        c.SetBounds(inner - 92, 102, 92, 28); break;
                    case "txtSteamCmd":
                        c.SetBounds(28, 188, Math.Max(300, inner - 130), 26); break;
                    case "btnExamCmd":
                        c.SetBounds(inner - 92, 186, 92, 28); break;
                    case "txtVersiones":
                        c.SetBounds(28, 272, Math.Max(300, inner - 130), 26); break;
                    case "btnExamVer":
                        c.SetBounds(inner - 92, 270, 92, 28); break;
                    case "lblEstadoRutas":
                        c.SetBounds(28, 310, inner, 40); break;
                    case "btnDetectar":
                        c.SetBounds(28, 358, 220, 32); break;
                    case "btnGuardarRutas":
                        c.SetBounds(258, 358, 130, 32); break;
                    case "btnAbrirCfg":
                        c.SetBounds(398, 358, 140, 32); break;
                    case "rowsMods":
                        c.SetBounds(28, 82, inner, Math.Max(140, h - 110)); break;
                }
            }
        }
    }

    // ============================================================ INICIO

    private void BuildInicio()
    {
        var p = NewPage("inicio");
        p.Controls.Add(Head("Estado", "Resumen de tu instalación de RimWorld"));

        var cards = new Panel { Name = "cards", Bounds = new Rectangle(28, 82, 900, 84), BackColor = Theme.Bg };
        p.Controls.Add(cards);

        void Reflow()
        {
            int gap = 10;
            int cw = Math.Max(110, (cards.Width - gap * 3) / 4);
            var l = new[] { _cVersion, _cMods, _cLog, _cIdioma };
            for (int i = 0; i < 4; i++) l[i].SetBounds(i * (cw + gap), 0, cw, cards.Height);
        }
        cards.Resize += (_, _) => Reflow();
        cards.HandleCreated += (_, _) => Reflow();

        _cVersion.Caption = "Versión";
        _cMods.Caption = "Mods activos";
        _cLog.Caption = "Registro";
        _cIdioma.Caption = "Idioma";
        _cVersion.Accent = Theme.Gold;
        _cMods.Accent = Theme.Blue;
        _cLog.Accent = Theme.Green;
        _cIdioma.Accent = Theme.Text3;
        _cLog.Hint = "errores en el log";

        foreach (var c in new[] { _cVersion, _cMods, _cLog, _cIdioma }) cards.Controls.Add(c);

        var sec = Section("Detalle técnico");
        sec.Name = "secLog";
        p.Controls.Add(sec);

        _txtLog.Name = "logBox";
        _txtLog.Multiline = true;
        _txtLog.ReadOnly = true;
        _txtLog.ScrollBars = ScrollBars.Both;
        _txtLog.WordWrap = false;
        _txtLog.BackColor = Theme.Card;
        _txtLog.ForeColor = Theme.Text2;
        _txtLog.BorderStyle = BorderStyle.None;
        _txtLog.Font = Theme.Mono();
        p.Controls.Add(_txtLog);
        DarkScroll.Apply(_txtLog);

        var bar = new Panel { Name = "barraInicio", BackColor = Theme.Bg };
        p.Controls.Add(bar);

        // Solo lo que es propio de esta pagina. Abrir la carpeta de mods ya existe
        // en Mods: duplicarlo aqui solo confunde.
        var b1 = new FlatBtn("Actualizar", 0, 0, 118, 32);
        b1.Click += (_, _) => RefreshAll();
        bar.Controls.Add(b1);

        var b2 = new FlatBtn("Abrir registro", 128, 0, 138, 32);
        b2.Click += (_, _) => OpenPath(Rim.LogPath);
        bar.Controls.Add(b2);

        // Abrir otra de las copias guardadas sin cambiar la configuracion.
        var b3 = new FlatBtn("Versiones", 276, 0, 118, 32);
        b3.Click += (_, _) => AbrirVersiones();
        bar.Controls.Add(b3);
    }

    /// <summary>
    /// Busca en el taller el id de los mods copiados a mano y, si el usuario acepta,
    /// escribe About\PublishedFileId.txt. Solo se aceptan coincidencias exactas.
    /// </summary>
    private async void VincularInstalados()
    {
        if (MessageBox.Show(this,
            "Se buscará en el taller el id de los mods instalados que no lo tienen.\n\n" +
            "Solo se vincula cuando el título coincide exactamente: escribir un id " +
            "equivocado haría que una actualización futura sustituyera el mod por otro.\n\n" +
            "Puede tardar un rato (una consulta por mod). ¿Continuar?",
            "Vincular instalados", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        SetOut("Buscando ids de los mods sin vincular…\r\n\r\nNo cierres la ventana.");

        try
        {
            var rechazos = new List<Workshop.Rechazo>();
            var hallados = await Task.Run(() => Workshop.BuscarVinculos(
                l => { if (!IsDisposed) BeginInvoke(new Action(() => { if (!IsDisposed) SetOut("Buscando…\r\n\r\n" + l); })); },
                rechazos));

            if (hallados.Count == 0)
            {
                SetOut("Ningún mod sin id coincide exactamente con un título del taller.\r\n\r\n" +
                       $"Revisados {rechazos.Count}. Es normal en mods que no están en Steam " +
                       "(los [ES], los de LoversLab y los parches propios).");
                return;
            }

            var lista = string.Join("\r\n", hallados.Take(20)
                .Select(v => $"  • {v.Carpeta}  →  {v.Id}"));
            if (hallados.Count > 20) lista += $"\r\n  … y {hallados.Count - 20} más";

            if (MessageBox.Show(this,
                $"Se encontraron {hallados.Count} coincidencias exactas:\r\n\r\n{lista}\r\n\r\n" +
                "¿Escribir los ids? Después podrás comprobar sus actualizaciones.",
                "Vincular instalados", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                SetOut($"Propuesta descartada ({hallados.Count} sin escribir).");
                return;
            }

            int ok = 0, fallos = 0;
            foreach (var v in hallados)
                if (Workshop.EscribirPublishedFileId(v.Carpeta, v.Id, out _)) ok++;
                else fallos++;

            SetOut($"Vinculados: {ok}" + (fallos > 0 ? $"   fallos: {fallos}" : "") +
                   "\r\n\r\nPulsa «Buscar actualizaciones» para comprobarlos.");
        }
        catch (Exception ex)
        {
            SetOut("Error al vincular: " + ex.Message);
        }
    }

    /// <summary>Lista las instalaciones encontradas y abre la elegida.</summary>
    private void AbrirVersiones()
    {
        try
        {
            var lista = Versiones.Listar();
            using var dlg = new VersionesForm(lista);
            dlg.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Versiones",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }


    private void BuildWorkshop()
    {
        var p = NewPage("workshop");
        p.Controls.Add(Head("Workshop", "Busca mods por nombre, o abre uno con su enlace o ID"));

        _txtUrl.Name = "txtUrl";
        _txtUrl.BackColor = Theme.Card;
        _txtUrl.ForeColor = Theme.Text;
        _txtUrl.BorderStyle = BorderStyle.FixedSingle;
        _txtUrl.Font = Theme.Body();
        _txtUrl.PlaceholderText = "Nombre del mod, enlace de Steam o ID numérico";
        p.Controls.Add(_txtUrl);

        var b = new FlatBtn("Buscar", 800, 81, 132, 28);
        b.Name = "btnBuscar";
        b.Base = Theme.Blue;
        b.HoverColor = Color.FromArgb(116, 168, 218);
        b.TextColor = Color.White;
        b.Click += (_, _) => BuscarWorkshop();
        p.Controls.Add(b);

        // --- clave
        _lblKeyState.Name = "lblKeyState";
        _lblKeyState.Font = Theme.Caption();
        _lblKeyState.ForeColor = Theme.Text3;
        _lblKeyState.BackColor = Theme.Bg;
        p.Controls.Add(_lblKeyState);

        var txtKey = new TextBox
        {
            Name = "txtKey",
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            Font = Theme.Body(),
            PlaceholderText = "Clave de Steam (32 caracteres)",
        };
        p.Controls.Add(txtKey);

        var guardar = new FlatBtn("Guardar", 0, 0, 1, 1);
        guardar.Name = "btnGuardarClave";
        guardar.Text = "Guardar clave";
        guardar.Click += (_, _) =>
        {
            var k = txtKey.Text.Trim();
            if (k.Length != 32)
            {
                SetOut($"La clave debe tener 32 caracteres y esta tiene {k.Length}.\n" +
                       "Se obtiene en steamcommunity.com/dev/apikey");
                return;
            }
            Rim.Cfg.SteamApiKey = k;
            Rim.Cfg.Save();
            ActualizarEstadoClave();
            SetOut("Clave guardada.\n\nPulsa «Probar conexión» para verificarla.");
        };
        p.Controls.Add(guardar);

        var probar = new FlatBtn("Probar conexión", 0, 0, 1, 1);
        probar.Name = "btnProbar";
        probar.Click += async (_, _) => await ProbarConexion();
        p.Controls.Add(probar);

        var descargar = new FlatBtn("Descargar e instalar", 0, 0, 1, 1);
        descargar.Name = "btnDescargar";
        descargar.Base = Theme.Green;
        descargar.HoverColor = Color.FromArgb(112, 202, 148);
        descargar.TextColor = Color.FromArgb(10, 20, 14);
        descargar.Click += (_, _) => DescargarSeleccionado();
        p.Controls.Add(descargar);

        var actualizar = new FlatBtn("Buscar actualizaciones", 0, 0, 1, 1);
        actualizar.Name = "btnActualizaciones";
        actualizar.Click += (_, _) => BuscarActualizaciones();
        p.Controls.Add(actualizar);

        // Mods copiados a mano no traen PublishedFileId.txt: sin el id no se les
        // pueden comprobar actualizaciones. Esto lo busca por titulo en el taller.
        var vincular = new FlatBtn("Vincular instalados", 0, 0, 1, 1);
        vincular.Name = "btnVincular";
        vincular.Click += (_, _) => VincularInstalados();
        p.Controls.Add(vincular);

        var aviso = new FlatBtn("", 0, 0, 1, 1);
        aviso.Name = "btnAviso";
        aviso.Click += (_, _) =>
        {
            Rim.Cfg.AvisarActualizaciones = !Rim.Cfg.AvisarActualizaciones;
            Rim.Cfg.Save();
            ActualizarBotonAviso();
            SetOut(Rim.Cfg.AvisarActualizaciones
                ? "Aviso de actualizaciones al arrancar: ACTIVADO"
                : "Aviso de actualizaciones al arrancar: DESACTIVADO");
        };
        p.Controls.Add(aviso);

        _lvRes.Name = "lvRes";
        _lvRes.View = View.Details;
        _lvRes.FullRowSelect = true;
        _lvRes.BackColor = Theme.Card;
        _lvRes.ForeColor = Theme.Text;
        _lvRes.BorderStyle = BorderStyle.None;
        _lvRes.Font = Theme.Caption();
        _lvRes.Columns.Add("Título", 560);
        _lvRes.Columns.Add("1.7", 50);
        _lvRes.Columns.Add("ID", 170);
        p.Controls.Add(_lvRes);
        DarkScroll.Apply(_lvRes);

        _txtOut.Name = "txtOut";
        _txtOut.Multiline = true;
        _txtOut.ReadOnly = true;
        _txtOut.ScrollBars = ScrollBars.Vertical;
        _txtOut.BackColor = Theme.Card;
        _txtOut.ForeColor = Theme.Text2;
        _txtOut.BorderStyle = BorderStyle.None;
        _txtOut.Font = Theme.Mono();
        p.Controls.Add(_txtOut);
        DarkScroll.Apply(_txtOut);
    }

    // ============================================================ MODS

    private void BuildMods()
    {
        var p = NewPage("mods");
        p.Controls.Add(Head("Mods", "Mantenimiento de tu instalación. Cada acción explica qué hace antes de ejecutarse."));

        var rows = new Panel { Name = "rowsMods", BackColor = Theme.Bg };
        p.Controls.Add(rows);

        void Row(int i, string title, string detail, string btn, Action act, Color? col = null)
        {
            var r = new ActionRow(title, detail, btn)
            {
                Bounds = new Rectangle(0, i * 70, rows.Width, 60),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            if (col.HasValue)
            {
                r.ButtonColor = col.Value;
                r.ButtonTextColor = Color.FromArgb(10, 20, 14);
            }
            r.Invoke += act;
            rows.Controls.Add(r);
        }

        Row(0, "Copias de seguridad", "Elimina backups antiguos y conserva el más reciente de cada mod.", "Limpiar", LimpiarBackups);
        Row(1, "Dependencias", "Detecta mods activos que necesitan otro mod ausente o desactivado.", "Comprobar", ComprobarDeps);
        Row(2, "Carpeta de mods", "Abre la carpeta donde están instalados los mods del juego.", "Abrir", () => OpenPath(Rim.ModsDir));
        Row(3, "Datos del juego", "Configuración, partidas guardadas y registro de errores.", "Abrir", () => OpenPath(Rim.ProfileDir));
        Row(4, "Enlaces de idioma", "Vuelve a crear los puentes de traducción si se perdieron.", "Recrear", RecrearPuentes, Theme.Blue);
        Row(5, "Ordenar por reglas", "Coloca cada mod según sus loadAfter/loadBefore. Puedes revisar el resultado antes de aplicarlo.", "Ordenar", OrdenarMods, Theme.Blue);
        Row(6, "Incompatibilidades", "Comprueba si dos mods activos se declaran incompatibles entre sí.", "Comprobar", ComprobarIncompatibles);
    }

    // ============================================================ LISTA

    private void BuildLista()
    {
        var p = NewPage("lista");
        p.Controls.Add(Head("Lista de mods", "Marca o desmarca y aplica. Se guarda una copia antes de escribir."));

        _txtFiltro.Name = "filtro";
        _txtFiltro.Bounds = new Rectangle(28, 82, 300, 26);
        _txtFiltro.BackColor = Theme.Card;
        _txtFiltro.ForeColor = Theme.Text;
        _txtFiltro.BorderStyle = BorderStyle.FixedSingle;
        _txtFiltro.Font = Theme.Body();
        _txtFiltro.PlaceholderText = "Filtrar por nombre";
        _txtFiltro.TextChanged += (_, _) => { _pagina = 0; CargarLista(); };
        p.Controls.Add(_txtFiltro);

        _lblLista.Name = "lblLista";
        _lblLista.Bounds = new Rectangle(760, 86, 220, 18);
        _lblLista.Font = Theme.Caption();
        _lblLista.ForeColor = Theme.Text3;
        _lblLista.BackColor = Theme.Bg;
        _lblLista.TextAlign = ContentAlignment.MiddleRight;
        // sin AutoEllipsis el texto se recorta en silencio al estrechar la ventana
        _lblLista.AutoEllipsis = true;
        p.Controls.Add(_lblLista);

        _lvMods.Name = "lvMods";
        // Las filas sin About.xml no se pueden activar: se revierte el clic.
        _lvMods.ItemCheck += (_, e) =>
        {
            if (_lvMods.Items[e.Index].Tag is string s && s.Length == 0)
            {
                e.NewValue = CheckState.Unchecked;
                BeginInvoke(new Action(() => _lvMods.Items[e.Index].Checked = false));
            }
        };
        _lvMods.View = View.Details;
        _lvMods.CheckBoxes = true;
        _lvMods.FullRowSelect = true;
        _lvMods.HideSelection = false;
        _lvMods.BackColor = Theme.Card;
        _lvMods.ForeColor = Theme.Text;
        _lvMods.BorderStyle = BorderStyle.None;
        _lvMods.Font = Theme.Caption();
        _lvMods.Columns.Add("Nombre", 520);
        _lvMods.Columns.Add("packageId", 340);
        p.Controls.Add(_lvMods);
        DarkScroll.Apply(_lvMods);

        var bar = new Panel { Name = "barraLista", BackColor = Theme.Bg };
        p.Controls.Add(bar);

        var aplicar = new FlatBtn("Aplicar cambios", 0, 0, 148, 32);
        aplicar.Base = Theme.Green;
        aplicar.HoverColor = Color.FromArgb(112, 202, 148);
        aplicar.TextColor = Color.FromArgb(10, 20, 14);
        aplicar.Click += (_, _) => AplicarLista();
        bar.Controls.Add(aplicar);

        var prev = new FlatBtn("<", 158, 0, 34, 32);
        prev.Click += (_, _) => { if (_pagina > 0) { _pagina--; CargarLista(); } };
        bar.Controls.Add(prev);

        var next = new FlatBtn(">", 196, 0, 34, 32);
        next.Click += (_, _) => { _pagina++; CargarLista(); };
        bar.Controls.Add(next);

        var marcar = new FlatBtn("Marcar página", 244, 0, 120, 32);
        marcar.Click += (_, _) => { foreach (ListViewItem it in _lvMods.Items) it.Checked = true; };
        bar.Controls.Add(marcar);

        var desmarcar = new FlatBtn("Desmarcar", 372, 0, 110, 32);
        desmarcar.Click += (_, _) => { foreach (ListViewItem it in _lvMods.Items) it.Checked = false; };
        bar.Controls.Add(desmarcar);

        var perfiles = new FlatBtn("Perfiles", 608, 0, 110, 32);
        perfiles.Name = "btnPerfiles";
        perfiles.Click += (_, _) => Perfiles();
        bar.Controls.Add(perfiles);

        var rec = new FlatBtn("Recargar", 490, 0, 110, 32);
        rec.Click += (_, _) => { _pagina = 0; CargarLista(); };
        bar.Controls.Add(rec);
    }

    // ============================================================ datos

    private void RefreshAll()
    {
        _busy = true;
        try
        {
            var st = Rim.Analyze();
            _cVersion.Value = Rim.LocalVersion();
            _cMods.Value = Rim.ActiveMods().Count.ToString();
            _cLog.Value = st.Errors == 0 ? "Limpio" : st.Errors.ToString();
            _cLog.Accent = st.Errors == 0 ? Theme.Green : Theme.Red;
            _cIdioma.Value = Short(Rim.ActiveLanguage());
            // La lista activa incluye Core y los DLC (ludeon.*), que no tienen carpeta
            // en Mods; por eso activos (405) e instalados en disco (404) no coinciden.
            _cMods.Hint = $"{Rim.AllMods().Count} en disco";

            _txtLog.Text =
                $"Versión       {Rim.LocalVersion()}\r\n" +
                $"Mods activos  {Rim.ActiveMods().Count}\r\n" +
                $"Tamaño log    {st.Bytes / 1024} KB\r\n\r\n" +
                $"Errores       {st.Errors}\r\n" +
                $"Errores XML   {st.XmlErrors}\r\n" +
                $"Avisos        {st.Warns}\r\n\r\n" +
                $"Idioma        {Rim.ActiveLanguage()}\r\n" +
                $"Juego abierto {(Rim.GameRunning() ? "sí" : "no")}\r\n\r\n" +
                $"Instalación   {Rim.Cfg.GamePath}";

            foreach (var c in new[] { _cVersion, _cMods, _cLog, _cIdioma }) c.Invalidate();
        }
        finally { _busy = false; }
    }

    private void RefreshLogOnly()
    {
        var st = Rim.Analyze();
        var v = st.Errors == 0 ? "Limpio" : st.Errors.ToString();
        if (_cLog.Value != v)
        {
            _cLog.Value = v;
            _cLog.Accent = st.Errors == 0 ? Theme.Green : Theme.Red;
            _cLog.Invalidate();
        }
    }

    private static string Short(string s)
    {
        if (s.StartsWith("Spanish")) return "Español";
        if (s.StartsWith("Russian")) return "Ruso";
        return s.Length > 12 ? s[..12] : s;
    }

    private void PlayGame()
    {
        if (Rim.GameRunning())
        {
            MessageBox.Show(this, "RimWorld ya está en ejecución.", "Lodeon Admin",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!Rim.LaunchGame(out var err))
            MessageBox.Show(this, "No se pudo iniciar: " + err, "Lodeon Admin",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    // ============================================================ lista

    private void CargarLista()
    {
        _all.Clear();
        _all.AddRange(Rim.AllMods());
        _orig.Clear();
        foreach (var m in _all)
            if (!m.SinAbout && m.PackageId.Length > 0) _orig[m.PackageId] = m.Active;

        var f = _txtFiltro.Text.Trim();
        _filtrados = _all.Where(m => f.Length == 0 ||
            m.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
            m.PackageId.Contains(f, StringComparison.OrdinalIgnoreCase)).ToList();

        int paginas = Math.Max(1, (int)Math.Ceiling(_filtrados.Count / (double)PorPagina));
        if (_pagina >= paginas) _pagina = paginas - 1;
        if (_pagina < 0) _pagina = 0;

        _lvMods.BeginUpdate();
        _lvMods.Items.Clear();
        foreach (var m in _filtrados.Skip(_pagina * PorPagina).Take(PorPagina))
        {
            var it = new ListViewItem(m.Name) { Checked = m.Active, Tag = m.PackageId };
            if (m.SinAbout)
            {
                it.SubItems.Add("(sin About.xml)");
                it.ForeColor = Theme.Text3;   // se ve, pero apagado
                it.Tag = "";                  // no se puede activar
            }
            else
            {
                it.SubItems.Add(m.PackageId);
            }
            _lvMods.Items.Add(it);
        }
        _lvMods.EndUpdate();
        int sinAbout = _all.Count(m => m.SinAbout);
        _lblLista.Text = $"{_filtrados.Count} mods · página {_pagina + 1} de {paginas}" +
                         (sinAbout > 0 ? $" · {sinAbout} sin About.xml" : "");
    }

    private void AplicarLista()
    {
        try
        {
            var act = Rim.ActiveMods();
            var quitar = new List<string>();
            var poner = new List<string>();

            foreach (ListViewItem it in _lvMods.Items)
            {
                if (it.Tag is not string pk) continue;
                bool antes = _orig.TryGetValue(pk, out var a) && a;
                if (it.Checked && !antes) poner.Add(pk);
                else if (!it.Checked && antes) quitar.Add(pk);
            }

            if (quitar.Count == 0 && poner.Count == 0)
            {
                MessageBox.Show(this, "No hay cambios en esta página.", "Lodeon Admin",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (MessageBox.Show(this,
                $"Se activarán {poner.Count} mods y se desactivarán {quitar.Count}.\n\n" +
                "Se guarda una copia de la configuración antes de escribir.\n\n¿Continuar?",
                "Aplicar cambios", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            // Reinsertar en la posicion original, no al final: en RimWorld el orden
            // de carga importa y mover un mod puede desactivar sus parches.
            var nueva = Order.Reinsertar(
                act.Where(x => !quitar.Contains(x)).ToList(), poner);

            if (!Rim.SetActiveMods(nueva, out _, out var err))
            {
                MessageBox.Show(this, "Error al escribir: " + err, "Lodeon Admin",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Guardar el indice para que el proximo cambio respete esta posicion.
            Order.Mezclar(nueva);

            // El juego lee ModsConfig.xml al arrancar: si esta abierto, los cambios
            // no tienen efecto hasta reiniciarlo.
            if (Rim.GameRunning())
                MessageBox.Show(this,
                    "Cambios guardados.\n\nRimWorld está abierto: ciérralo y vuelve a abrirlo " +
                    "para que la nueva lista tenga efecto.",
                    "Hay que reiniciar", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            CargarLista();
            RefreshAll();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }


    private async void BuscarWorkshop()
    {
        var q = _txtUrl.Text.Trim();
        if (q.Length == 0) { SetOut("Escribe un nombre, un enlace o un ID para buscar."); return; }

        var key = Rim.Cfg.SteamApiKey?.Trim() ?? "";

        // si es un enlace o ID, extraer el numero
        var m = System.Text.RegularExpressions.Regex.Match(q, @"(\d{6,})");
        var consulta = m.Success ? m.Groups[1].Value : q;

        // Buscar por texto NO necesita clave: la pagina publica del taller permite
        // lo mismo que la API. Solo se avisa de que faltan las etiquetas.
        if (key.Length != 32 && !m.Success)
        {
            SetOut("Consultando el taller (sin clave de API)…");
            try
            {
                var hallados = await Task.Run(() => Workshop.BuscarWeb(consulta));

                _lvRes.BeginUpdate();
                _lvRes.Items.Clear();
                foreach (var (id, titulo) in hallados)
                {
                    var it = new ListViewItem(
                        string.IsNullOrWhiteSpace(titulo) ? "(sin título)" : titulo) { Tag = id };
                    it.SubItems.Add("");
                    it.SubItems.Add(id);
                    _lvRes.Items.Add(it);
                }
                _lvRes.EndUpdate();

                SetOut(hallados.Count == 0
                    ? $"Sin resultados para «{consulta}».\n\nPrueba otro término, o pega el enlace del mod."
                    : $"{hallados.Count} resultado(s) para «{consulta}».\n\n" +
                      "Búsqueda sin clave: funciona, pero la columna 1.7 necesita la clave de Steam.\n" +
                      "Para descargar sí basta con seleccionar y pulsar Descargar.");
                return;
            }
            catch (Exception ex)
            {
                SetOut("No se pudo consultar el taller: " + ex.Message);
                return;
            }
        }

        if (key.Length != 32)
        {
            SetOut("No hay una clave de Steam válida guardada.\n\n" +
                   "Pégala en el campo de abajo y pulsa «Guardar clave».\n" +
                   "Se obtiene gratis en steamcommunity.com/dev/apikey");
            return;
        }

        SetOut("Consultando el Workshop…");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
            var url = m.Success
                ? $"https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/"
                : $"https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/?key={key}&appid=294100" +
                  $"&search_text={Uri.EscapeDataString(consulta)}&numperpage=40&return_tags=true";

            HttpResponseMessage resp;
            string json;
            if (m.Success)
            {
                var form = new Dictionary<string, string> { { "itemcount", "1" }, { "publishedfileids[0]", consulta } };
                resp = await http.PostAsync(url, new FormUrlEncodedContent(form));
                json = await resp.Content.ReadAsStringAsync();
            }
            else
            {
                resp = await http.GetAsync(url);
                json = await resp.Content.ReadAsStringAsync();
            }

            if (!resp.IsSuccessStatusCode)
            {
                SetOut($"Steam respondió {(int)resp.StatusCode}.\n\n" +
                       ((int)resp.StatusCode == 403
                           ? "La clave no es válida o fue revocada.\nGenera otra en steamcommunity.com/dev/apikey"
                           : "Inténtalo de nuevo en un momento."));
                return;
            }

            using var doc = JsonDocument.Parse(json);
            var arr = doc.RootElement.GetProperty("response").GetProperty("publishedfiledetails");

            _lvRes.BeginUpdate();
            _lvRes.Items.Clear();
            int n = 0;
            foreach (var f in arr.EnumerateArray())
            {
                if (f.TryGetProperty("result", out var res) && res.GetInt32() != 1) continue;
                var id = f.TryGetProperty("publishedfileid", out var p) ? p.GetString() : "";
                var title = f.TryGetProperty("title", out var t) ? t.GetString() : "";
                bool v17 = false;
                if (f.TryGetProperty("tags", out var tags))
                    foreach (var tg in tags.EnumerateArray())
                        if (tg.TryGetProperty("tag", out var tv) && tv.GetString() == "1.7") v17 = true;
                var it = new ListViewItem(string.IsNullOrWhiteSpace(title) ? "(sin título)" : title) { Tag = id };
                it.SubItems.Add(v17 ? "sí" : "");
                it.SubItems.Add(id ?? "");
                _lvRes.Items.Add(it);
                n++;
            }
            _lvRes.EndUpdate();

            SetOut(n == 0
                ? $"Sin resultados para «{consulta}». Prueba otro término."
                : $"{n} resultado(s) para «{consulta}».");
        }
        catch (TaskCanceledException) { SetOut("Tiempo de espera agotado. Revisa tu conexión."); }
        catch (Exception ex) { SetOut("Error de conexión: " + ex.Message); }
    }

    private void SetOut(string t) => _txtOut.Text = t;

    /// <summary>Refleja en el boton si el aviso al arrancar esta activo.</summary>
    private void ActualizarBotonAviso()
    {
        var pg = _pages.Controls.Find("pg_workshop", false).FirstOrDefault();
        if (pg?.Controls.Find("btnAviso", false).FirstOrDefault() is not FlatBtn b) return;
        b.Text = Rim.Cfg.AvisarActualizaciones
            ? "Aviso al arrancar: sí"
            : "Aviso al arrancar: no";
        b.Invalidate();
    }

    /// <summary>Descarga e instala los items seleccionados en la lista de resultados.</summary>
    private async void DescargarSeleccionado()
    {
        // async void no propaga excepciones: sin este try, un fallo de red o de
        // SteamCMD cierra la aplicacion sin avisar.
        try
        {
        var ids = new List<string>();
        foreach (ListViewItem it in _lvRes.SelectedItems)
            if (it.Tag is string s && s.Length >= 6) ids.Add(s);

        if (ids.Count == 0)
        {
            var m = System.Text.RegularExpressions.Regex.Match(_txtUrl.Text, @"(\d{6,})");
            if (m.Success) ids.Add(m.Groups[1].Value);
        }
        if (ids.Count == 0) { SetOut("Selecciona un mod de la lista o pega su ID."); return; }

        // Si lo pegado es una coleccion, hay que expandirla: SteamCMD no descarga
        // colecciones, solo mods sueltos.
        SetOut("Comprobando si es una colección...");
        var expandidos = Workshop.Expandir(ids, out var notaColeccion);
        if (!string.IsNullOrWhiteSpace(notaColeccion))
            SetOut(notaColeccion + "\r\n\r\nTotal a descargar: " + expandidos.Count);

        if (MessageBox.Show(this,
            $"Se descargará con SteamCMD y se instalará en la carpeta de mods.\n" +
            $"Si el mod ya existe, se guarda copia antes de sustituirlo.\n\n" +
            (string.IsNullOrWhiteSpace(notaColeccion) ? "" : notaColeccion + "\n") +
            $"Mods: {expandidos.Count}\n\n¿Continuar?",
            "Descargar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        ids = expandidos;

        SetOut($"Descargando {ids.Count} mod(s) con SteamCMD...\r\n\r\nNo cierres la ventana.");

        // El progreso llega desde el hilo de SteamCMD: hay que volver al hilo de
        // interfaz para escribir en el cuadro de texto.
        void Progreso(string linea)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke(new Action(() =>
                {
                    if (!IsDisposed) _txtOut.Text = "Descargando...\r\n\r\n" + linea;
                }));
            }
            catch (InvalidOperationException) { }
        }

        var (ok, log) = await Task.Run(() => Workshop.Descargar(ids, Progreso));
        SetOut(log);
        if (ok.Count == 0) { SetOut(log + "\nNingún mod se descargó."); return; }

        int instalados = 0;
        var detalle = new StringBuilder(log).AppendLine();
        foreach (var id in ok)
        {
            var (bien, msg) = await Task.Run(() => Workshop.Instalar(id));
            detalle.AppendLine((bien ? "Instalado: " : "Fallo: ") + msg);
            if (bien) instalados++;
        }
        detalle.AppendLine($"\n{instalados} de {ok.Count} instalados. Reinicia el juego para verlos.");
        SetOut(detalle.ToString());
        RefreshAll();
        }
        catch (Exception ex)
        {
            SetOut("La descarga falló: " + ex.Message);
        }
    }

    /// <summary>Busca mods desactualizados comparando fechas con el Workshop.</summary>
    private async void BuscarActualizaciones()
    {
        // async void: una excepcion aqui cerraria la aplicacion.
        try
        {
        SetOut("Consultando fechas del Workshop…");
        var (des, comprobables) = await Task.Run(() =>
        {
            var lista = Workshop.BuscarActualizaciones(out var comp);
            return (lista, comp);
        });

        if (comprobables == 0)
        {
            SetOut("Ningún mod tiene su ID del Workshop, así que no se pueden comparar fechas.");
            return;
        }
        if (des.Count == 0) { SetOut($"Los {comprobables} mods comprobables están al día."); return; }

        var sb = new StringBuilder();
        sb.AppendLine($"{des.Count} desactualizados de {comprobables} comprobables:");
        sb.AppendLine();
        foreach (var d in des.Take(25))
            sb.AppendLine($"{d.Titulo}\n  taller {d.Taller:yyyy-MM-dd} · local {d.Local:yyyy-MM-dd} · {d.Dias} días · {d.Id}");
        if (des.Count > 25) sb.AppendLine($"… y {des.Count - 25} más");
        SetOut(sb.ToString());
        }
        catch (Exception ex)
        {
            SetOut("No se pudieron comprobar las actualizaciones: " + ex.Message);
        }
    }

    /// <summary>Guardar y cargar perfiles de lista de mods.</summary>
    private void Perfiles()
    {
        var lista = Workshop.ListarPerfiles();
        using var dlg = new Form
        {
            Text = "Perfiles de mods",
            Size = new Size(520, 420),
            StartPosition = FormStartPosition.CenterParent,
            BackColor = Theme.Bg,
            ForeColor = Theme.Text,
            Font = Theme.Body(),
            Icon = Icon,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
        };

        var lbl = new Label
        {
            Text = lista.Count == 0 ? "Todavía no hay perfiles guardados." : $"{lista.Count} perfiles guardados",
            Bounds = new Rectangle(16, 14, 470, 18),
            ForeColor = Theme.Text3,
            BackColor = Theme.Bg,
            Font = Theme.Caption(),
        };
        dlg.Controls.Add(lbl);

        var lv = new ListView
        {
            Bounds = new Rectangle(16, 38, 470, 240),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            Font = Theme.Caption(),
        };
        lv.Columns.Add("Perfil", 220);
        lv.Columns.Add("Mods", 60);
        lv.Columns.Add("Creado", 170);
        foreach (var p in lista)
        {
            var it = new ListViewItem(p.nombre) { Tag = p.archivo };
            it.SubItems.Add(p.mods.ToString());
            it.SubItems.Add(p.creado);
            lv.Items.Add(it);
        }
        dlg.Controls.Add(lv);
        DarkScroll.Apply(lv);

        var txt = new TextBox
        {
            Bounds = new Rectangle(16, 290, 280, 24),
            BackColor = Theme.Card,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.FixedSingle,
            PlaceholderText = "Nombre del nuevo perfil",
        };
        dlg.Controls.Add(txt);

        var guardar = new FlatBtn("Guardar actual", 304, 289, 182, 26);
        guardar.Base = Theme.Green;
        guardar.HoverColor = Color.FromArgb(112, 202, 148);
        guardar.TextColor = Color.FromArgb(10, 20, 14);
        guardar.Click += (_, _) =>
        {
            var nombre = txt.Text.Trim().Length > 0 ? txt.Text.Trim() : "Perfil " + DateTime.Now.ToString("dd-MM HHmm");
            var (ok, msg) = Workshop.GuardarPerfil(nombre);
            MessageBox.Show(dlg, msg, "Perfiles", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            if (ok) dlg.DialogResult = DialogResult.OK;
        };
        dlg.Controls.Add(guardar);

        var cargar = new FlatBtn("Cargar seleccionado", 16, 326, 200, 28);
        cargar.Base = Theme.Blue;
        cargar.HoverColor = Color.FromArgb(116, 168, 218);
        cargar.TextColor = Color.White;
        cargar.Click += (_, _) =>
        {
            if (lv.SelectedItems.Count == 0) { MessageBox.Show(dlg, "Elige un perfil.", "Perfiles"); return; }
            var arch = lv.SelectedItems[0].Tag as string ?? "";
            if (MessageBox.Show(dlg, $"Se activarán los mods de «{lv.SelectedItems[0].Text}».\n\nSe guarda copia del estado actual.\n\n¿Continuar?",
                    "Cargar perfil", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            var (ok, msg) = Workshop.CargarPerfil(arch);
            MessageBox.Show(dlg, msg, "Perfiles", MessageBoxButtons.OK,
                ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
            if (ok) dlg.DialogResult = DialogResult.OK;
        };
        dlg.Controls.Add(cargar);

        var cerrar = new FlatBtn("Cerrar", 380, 326, 106, 28);
        cerrar.Click += (_, _) => dlg.DialogResult = DialogResult.Cancel;
        dlg.Controls.Add(cerrar);

        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            CargarLista();
            RefreshAll();
        }
    }

    private void ActualizarEstadoClave()
    {
        var k = Rim.Cfg.SteamApiKey?.Trim() ?? "";
        if (k.Length == 0)
        {
            // Sin clave el Workshop NO esta desactivado: buscar, descargar, instalar
            // y vincular funcionan. Solo falta el filtro por etiqueta de version.
            _lblKeyState.Text = "Sin clave · búsqueda y descarga funcionan; falta el filtro de versión";
            _lblKeyState.ForeColor = Theme.Text3;
        }
        else
        {
            var oculta = k.Length > 8 ? k[..4] + "········" + k[^4..] : k;
            _lblKeyState.Text = $"Clave {oculta}  ({k.Length} caracteres)  ·  guardada en config.json";
            _lblKeyState.ForeColor = Theme.Green;
        }
    }

    private async Task ProbarConexion()
    {
        var k = Rim.Cfg.SteamApiKey?.Trim() ?? "";
        if (k.Length != 32) { SetOut("No hay una clave válida guardada (deben ser 32 caracteres)."); return; }

        SetOut("Probando conexión con Steam…");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            var url = $"https://api.steampowered.com/IPublishedFileService/QueryFiles/v1/?key={k}&appid=294100&search_text=rimworld&numperpage=1";
            var resp = await http.GetAsync(url);
            SetOut(resp.IsSuccessStatusCode
                ? "Conexión correcta.\nLa clave funciona y el Workshop responde."
                : $"Steam respondió {(int)resp.StatusCode}.\n\n" +
                  ((int)resp.StatusCode == 403
                      ? "La clave no es válida o fue revocada."
                      : "Inténtalo de nuevo en un momento."));
        }
        catch (Exception ex) { SetOut("No se pudo conectar: " + ex.Message); }
    }

    private void OpenPath(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
            else if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
            else MessageBox.Show(this, "No existe:\n" + path, "Lodeon Admin",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch { }
    }
}