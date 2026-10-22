using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.Json;

namespace LodeonAdmin;

public class MainForm : Form
{
    // navegacion
    private readonly Panel _side = new();
    private readonly Panel _pages = new();
    private readonly List<NavBtn> _navs = new();
    private readonly List<string> _keys = new() { "inicio", "salud", "workshop", "mods", "lista", "idioma" };

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
    public static string Ver => "1.4";

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

        var host = new Panel { Dock = DockStyle.Top, Height = 84 + 36 * 6, BackColor = Theme.Side };
        _side.Controls.Add(host);

        string[] textos = { "Inicio", "Salud", "Workshop", "Mods", "Lista", "Idioma" };
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
                        c.SetBounds(28, 138, 340, 24); break;
                    case "btnGuardarClave":
                        c.SetBounds(376, 137, 128, 26); break;
                    case "btnProbar":
                        c.SetBounds(512, 137, 150, 26); break;
                    case "btnDescargar":
                        c.SetBounds(Math.Max(672, inner - 300), 137, 200, 26); break;
                    case "btnActualizaciones":
                        c.SetBounds(28, 180, 220, 26); break;
                    case "btnAviso":
                        c.SetBounds(256, 180, 260, 26); break;
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
    }

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
                $"orden: {inf.OrdenRoto.Count}  ·  {inf.SinSoporte17.Count} mods declaran solo hasta 1.6 (no es un problema)",
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

    // ============================================================ IDIOMA

    private void BuildIdioma()
    {
        var p = NewPage("idioma");
        p.Controls.Add(Head("Idioma", "El juego solo carga la carpeta que coincide con el idioma activo."));

        _lblLangDet.Name = "lblLangDet";
        _lblLangDet.Bounds = new Rectangle(28, 82, 700, 40);
        _lblLangDet.Font = Theme.Body();
        _lblLangDet.ForeColor = Theme.Text2;
        _lblLangDet.BackColor = Theme.Bg;
        p.Controls.Add(_lblLangDet);

        var sec = Section("Idiomas disponibles en tus mods");
        sec.Bounds = new Rectangle(28, 140, 400, 12);
        p.Controls.Add(sec);

        _lvLangs.Name = "lvLangs";
        _lvLangs.View = View.Details;
        _lvLangs.FullRowSelect = true;
        _lvLangs.MultiSelect = false;
        _lvLangs.BackColor = Theme.Card;
        _lvLangs.ForeColor = Theme.Text;
        _lvLangs.BorderStyle = BorderStyle.None;
        _lvLangs.Font = Theme.Caption();
        _lvLangs.Columns.Add("Idioma", 200);
        _lvLangs.Columns.Add("Mods", 55);
        _lvLangs.Columns.Add("Estado", 150);
        p.Controls.Add(_lvLangs);
        DarkScroll.Apply(_lvLangs);

        var lblOcultos = new Label
        {
            Name = "lblOcultos",
            Bounds = new Rectangle(0, 0, 420, 14),
            Font = Theme.Caption(),
            ForeColor = Theme.Text3,
            BackColor = Theme.Bg,
        };
        p.Controls.Add(lblOcultos);

        var usar = new FlatBtn("Usar este", 28, 0, 125, 30);
        usar.Name = "btnUsarIdioma";
        usar.Base = Theme.Blue;
        usar.HoverColor = Color.FromArgb(116, 168, 218);
        usar.TextColor = Color.White;
        usar.Click += (_, _) => UsarIdiomaSeleccionado();
        p.Controls.Add(usar);

        var auto = new FlatBtn("Elegir por mí", 161, 0, 130, 30);
        auto.Name = "btnAutoIdioma";
        auto.Click += (_, _) => ElegirAutomatico();
        p.Controls.Add(auto);

        // bloque de puentes
        _bridgeBox.Name = "bridgeBox";
        _bridgeBox.BackColor = Theme.Bg;
        p.Controls.Add(_bridgeBox);

        var secB = Section("Traducciones sin usar");
        secB.Bounds = new Rectangle(0, 0, 300, 12);
        _bridgeBox.Controls.Add(secB);

        var lblB = new Label
        {
            Name = "lblBridge",
            Bounds = new Rectangle(0, 20, 360, 150),
            Font = Theme.Caption(),
            ForeColor = Theme.Text2,
            BackColor = Theme.Bg,
        };
        _bridgeBox.Controls.Add(lblB);

        var crear = new FlatBtn("Crear enlaces", 0, 178, 140, 30);
        crear.Name = "btnPuente";
        crear.Base = Theme.Green;
        crear.HoverColor = Color.FromArgb(112, 202, 148);
        crear.TextColor = Color.FromArgb(10, 20, 14);
        crear.Click += (_, _) => CrearPuentes();
        _bridgeBox.Controls.Add(crear);

        var quitar = new FlatBtn("Quitar enlaces", 150, 178, 140, 30);
        quitar.Name = "btnQuitarPuente";
        quitar.Click += (_, _) => QuitarPuentes();
        _bridgeBox.Controls.Add(quitar);

        // bloque ruso
        var ruso = new Panel { Name = "rusoBox", BackColor = Theme.Card };
        p.Controls.Add(ruso);

        var lblR = new Label
        {
            Name = "lblRuso",
            Bounds = new Rectangle(16, 14, 600, 18),
            Font = Theme.BodyStrong(),
            ForeColor = Theme.Text2,
            BackColor = Theme.Card,
        };
        ruso.Controls.Add(lblR);

        var descR = new Label
        {
            Text = "Carpetas de idioma ruso incluidas por los mods. No se usan mientras el juego esté en español.",
            Bounds = new Rectangle(16, 34, 620, 16),
            Font = Theme.Caption(),
            ForeColor = Theme.Text3,
            BackColor = Theme.Card,
        };
        ruso.Controls.Add(descR);

        var off = new FlatBtn("Desactivar", 16, 60, 120, 30);
        off.Click += (_, _) => ToggleRuso(false);
        ruso.Controls.Add(off);

        var on = new FlatBtn("Restaurar", 144, 60, 120, 30);
        on.Click += (_, _) => ToggleRuso(true);
        ruso.Controls.Add(on);
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
            _cMods.Hint = $"{Rim.AllMods().Count} instalados";

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

            var nueva = act.Where(x => !quitar.Contains(x)).ToList();
            nueva.AddRange(poner);

            if (!Rim.SetActiveMods(nueva, out _, out var err))
            {
                MessageBox.Show(this, "Error al escribir: " + err, "Lodeon Admin",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

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

    // ============================================================ idioma

    /// <summary>
    /// Si ModsConfig.xml no se puede leer, el juego arranca sin mods y sin avisar.
    /// Se detecta aqui y se ofrece restaurar la copia mas reciente.
    /// </summary>
    private void ComprobarModsConfig()
    {
        var (estado, detalle, n) = Rim.EstadoModsConfig();
        if (estado == Rim.EstadoConfig.Ok) return;

        var copias = Rim.CopiasModsConfig();

        if (estado == Rim.EstadoConfig.NoExiste && copias.Count == 0)
        {
            MessageBox.Show(this,
                "No existe ModsConfig.xml y no hay copias.\n\n" +
                "Abre RimWorld una vez para que lo cree.",
                "Configuración ausente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (copias.Count == 0)
        {
            MessageBox.Show(this,
                "ModsConfig.xml no se puede leer:\n" + detalle + "\n\n" +
                "No hay copias de seguridad para restaurar.",
                "Configuración dañada", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var msg = estado == Rim.EstadoConfig.NoExiste
            ? "No existe ModsConfig.xml."
            : "ModsConfig.xml no se puede leer:\n" + detalle;

        if (MessageBox.Show(this,
            msg + $"\n\nHay {copias.Count} copia(s).\n\n" +
            "La más reciente es de " + File.GetLastWriteTime(copias[0]).ToString("yyyy-MM-dd HH:mm") + ".\n\n" +
            "¿Restaurar esa copia? El archivo actual se guarda antes por si acaso.",
            "Restaurar configuración", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        if (!Rim.RestaurarModsConfig(copias[0], out var err, out var rescate))
        {
            MessageBox.Show(this, "No se pudo restaurar: " + err, "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var (_, _, ahora) = Rim.EstadoModsConfig();
        MessageBox.Show(this,
            $"Restaurado. La lista tiene ahora {ahora} mods." +
            (rescate.Length > 0 ? "\n\nEl archivo anterior quedó en:\n" + Path.GetFileName(rescate) : ""),
            "Restaurado", MessageBoxButtons.OK, MessageBoxIcon.Information);

        RefreshAll();
    }

    /// <summary>
    /// Comprueba en segundo plano si hay mods desactualizados y avisa una vez al dia.
    /// No bloquea la ventana ni retrasa el arranque.
    /// </summary>
    private void AvisarActualizacionesAlArrancar()
    {
        if (!Rim.Cfg.AvisarActualizaciones) return;

        // ya se aviso hoy: no insistir en cada arranque
        var hoy = DateTime.Now.ToString("yyyy-MM-dd");
        if (Rim.Cfg.UltimoAvisoActualizaciones == hoy) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(3000);   // dejar que la ventana se dibuje
                var des = Workshop.BuscarActualizaciones(out var comprobables);
                if (comprobables == 0 || des.Count == 0) return;

                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    Rim.Cfg.UltimoAvisoActualizaciones = hoy;
                    Rim.Cfg.Save();

                    var r = MessageBox.Show(this,
                        $"Hay {des.Count} mod(s) con actualización disponible en el Workshop " +
                        $"de {comprobables} comprobables.\n\n" +
                        "Más reciente: " + des[0].Titulo + "\n\n" +
                        "¿Ver el detalle?",
                        "Actualizaciones", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                    if (r == DialogResult.Yes)
                    {
                        Select("workshop");
                        BuscarActualizaciones();
                    }
                }));
            }
            catch { /* sin conexion: no molestar */ }
        });
    }

    private void DetectarIdiomaAlArrancar()
    {
        try
        {
            if (!Rim.Cfg.DetectarIdioma) return;

            var (det, _) = LanguageSystem.DetectUserLanguage();
            var (juego, _) = LanguageSystem.GameLanguage();
            var inv = LanguageSystem.Inventory(out _);

            if (det != juego && inv.ContainsKey(det))
            {
                if (MessageBox.Show(this,
                    $"Tu equipo está en {Nb(det)} y el juego en {Nb(juego)}.\n\n¿Cambiar el juego a {Nb(det)}?",
                    "Lodeon Admin", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                    AplicarIdioma(det);
                return;
            }

            var cand = LanguageSystem.BridgeCandidates(juego);
            int ya = 0;
            foreach (var m in LanguageSystem.ScanMods()) ya += m.Folders.Count(f => f.IsDummy);

            if (cand.Count >= 5 && ya == 0)
            {
                if (MessageBox.Show(this,
                    $"Hay {cand.Count} mods con traducción a {Nb(juego)} que el juego no usa porque " +
                    "la carpeta tiene otro nombre.\n\n¿Crear los enlaces para aprovecharlas?",
                    "Lodeon Admin", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    CrearPuentesSilencioso(juego);
                    RefrescarIdioma();
                }
            }
        }
        catch { }
    }

    private void RefrescarIdioma()
    {
        var (det, detalle) = LanguageSystem.DetectUserLanguage();
        var (juego, folder) = LanguageSystem.GameLanguage();
        var inv = LanguageSystem.Inventory(out _);

        _lblLangDet.Text =
            $"Tu equipo: {Nb(det)}   ·   El juego: {Nb(juego)}" +
            (det == juego ? "   ·   coinciden" : "   ·   no coinciden") +
            $"\n{folder}";

        _lvLangs.BeginUpdate();
        _lvLangs.Items.Clear();
        // Los idiomas con un solo mod no aportan al selector: se listan los que
        // tienen 2 o mas, mas el idioma en uso y el recomendado.
        foreach (var kv in inv.OrderByDescending(x => x.Value))
        {
            bool relevante = kv.Value >= 2 || kv.Key == juego || kv.Key == det;
            if (!relevante) continue;

            var it = new ListViewItem(Nb(kv.Key));
            it.SubItems.Add(kv.Value.ToString());
            it.SubItems.Add(kv.Key == juego ? "en uso" : (kv.Key == det ? "recomendado" : ""));
            it.Tag = kv.Key;
            _lvLangs.Items.Add(it);
        }
        _lvLangs.EndUpdate();

        int ocultos = inv.Count(kv => kv.Value < 2 && kv.Key != juego && kv.Key != det);
        var pie = _pages.Controls.Find("pg_idioma", false).FirstOrDefault()
                         ?.Controls.Find("lblOcultos", false).FirstOrDefault() as Label;
        if (pie != null)
        {
            pie.Top = _lvLangs.Bottom + 6;
            pie.Text = ocultos > 0
                ? $"+ {ocultos} idioma(s) presentes en un solo mod, no listados arriba"
                : "";
        }

        var lbl = _bridgeBox.Controls.Find("lblBridge", false).FirstOrDefault() as Label;
        if (lbl != null)
        {
            var cand = LanguageSystem.BridgeCandidates(juego);
            int puentes = 0;
            foreach (var m in LanguageSystem.ScanMods()) puentes += m.Folders.Count(f => f.IsDummy);

            var sb = new StringBuilder();
            if (cand.Count > 0)
                sb.AppendLine($"{cand.Count} mods tienen su traducción a {Nb(juego)} en una carpeta");
            else
                sb.AppendLine("No hay traducciones pendientes.");

            if (cand.Count > 0) sb.AppendLine("con otro nombre, así que el juego las ignora.");
            if (puentes > 0) sb.Append('\n').AppendLine($"Enlaces activos: {puentes}");
            sb.AppendLine();
            foreach (var c in cand.Take(6)) sb.AppendLine("· " + Rec(c.ModName, 30));
            if (cand.Count > 6) sb.AppendLine($"· y {cand.Count - 6} más");
            lbl.Text = sb.ToString();
        }

        RefrescarRuso();
    }

    private static string Rec(string s, int n) => s.Length > n ? s[..(n - 1)] + "…" : s;

    private static string Nb(string c) => c switch
    {
        "spanish" => "Español",
        "spanishlatin" => "Español (LatAm)",
        "english" => "Inglés",
        "russian" => "Ruso",
        "chinesesimp" => "Chino simpl.",
        "chinesetrad" => "Chino trad.",
        "french" => "Francés",
        "german" => "Alemán",
        "korean" => "Coreano",
        "japanese" => "Japonés",
        "portbrazil" => "Portugués (BR)",
        "portuguese" => "Portugués",
        "polish" => "Polaco",
        "italian" => "Italiano",
        "czech" => "Checo",
        "ukrainian" => "Ucraniano",
        "turkish" => "Turco",
        "dutch" => "Neerlandés",
        "swedish" => "Sueco",
        "danish" => "Danés",
        "finnish" => "Finés",
        "norwegian" => "Noruego",
        "hungarian" => "Húngaro",
        "romanian" => "Rumano",
        "slovak" => "Eslovaco",
        "estonian" => "Estonio",
        "greek" => "Griego",
        "catalan" => "Catalán",
        "vietnamese" => "Vietnamita",
        "bulgarian" => "Búlgaro",
        "serbian" => "Serbio",
        "croatian" => "Croata",
        "lithuanian" => "Lituano",
        "latvian" => "Letón",
        "slovenian" => "Esloveno",
        "icelandic" => "Islandés",
        "indonesian" => "Indonesio",
        "thai" => "Tailandés",
        "arabic" => "Árabe",
        "hebrew" => "Hebreo",
        _ => c
    };

    private void UsarIdiomaSeleccionado()
    {
        if (_lvLangs.SelectedItems.Count == 0)
        {
            MessageBox.Show(this, "Elige un idioma de la lista.", "Idioma",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        AplicarIdioma(_lvLangs.SelectedItems[0].Tag as string ?? "english");
    }

    private void ElegirAutomatico()
    {
        var (det, detalle) = LanguageSystem.DetectUserLanguage();
        var inv = LanguageSystem.Inventory(out _);
        var msg = $"Idioma detectado: {Nb(det)}\n{detalle}\n\n";
        if (!inv.ContainsKey(det)) msg += "Ningún mod ofrece ese idioma; se usará inglés.\n\n";
        if (MessageBox.Show(this, msg + $"¿Configurar el juego en {Nb(det)}?", "Elegir por mí",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        AplicarIdioma(inv.ContainsKey(det) ? det : "english");
    }

    private void AplicarIdioma(string canon)
    {
        var (ok, msg) = LanguageSystem.SetGameLanguage(canon);
        if (!ok)
        {
            MessageBox.Show(this, "No se pudo cambiar: " + msg, "Idioma",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var cand = LanguageSystem.BridgeCandidates(canon);
        if (cand.Count > 0 &&
            MessageBox.Show(this,
                $"Idioma configurado: {msg}\n\n{cand.Count} mods tienen su traducción con otro nombre de carpeta.\n¿Crear los enlaces?",
                "Idioma", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            CrearPuentesSilencioso(canon);
        else
            MessageBox.Show(this, $"Idioma configurado: {msg}\n\nReinicia el juego para aplicarlo.",
                "Idioma", MessageBoxButtons.OK, MessageBoxIcon.Information);

        RefrescarIdioma();
    }

    private int CrearPuentesSilencioso(string canon)
    {
        int n = 0;
        foreach (var c in LanguageSystem.BridgeCandidates(canon))
        {
            if (c.Pick == null) continue;
            var (ok, _, _) = LanguageSystem.CreateBridge(c.Pick, canon);
            if (ok) n++;
        }
        return n;
    }

    private void CrearPuentes()
    {
        var (juego, _) = LanguageSystem.GameLanguage();
        var cand = LanguageSystem.BridgeCandidates(juego);
        if (cand.Count == 0)
        {
            MessageBox.Show(this, "No hay traducciones pendientes de enlazar.", "Idioma",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
            $"Se crearán {cand.Count} enlaces con el nombre «{LanguageSystem.PreferredFolderName(juego)}» " +
            "apuntando a la traducción que ya tienes.\n\n¿Continuar?",
            "Crear enlaces", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        int n = CrearPuentesSilencioso(juego);
        MessageBox.Show(this, $"Enlaces creados: {n} de {cand.Count}.\n\nReinicia el juego para verlos.",
            "Idioma", MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefrescarIdioma();
    }

    private void RecrearPuentes()
    {
        var (juego, _) = LanguageSystem.GameLanguage();
        int q = LanguageSystem.RemoveBridges();
        int n = CrearPuentesSilencioso(juego);
        MessageBox.Show(this, $"Se retiraron {q} y se crearon {n} enlaces.\n\nReinicia el juego.",
            "Idioma", MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefrescarIdioma();
    }

    private void QuitarPuentes()
    {
        int hay = 0;
        foreach (var m in LanguageSystem.ScanMods()) hay += m.Folders.Count(f => f.IsDummy);
        if (hay == 0)
        {
            MessageBox.Show(this, "No hay enlaces creados.", "Idioma",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this, $"Se borrarán {hay} enlaces. Las traducciones originales no se tocan.\n\n¿Continuar?",
                "Quitar enlaces", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        int n = LanguageSystem.RemoveBridges();
        MessageBox.Show(this, $"Enlaces eliminados: {n}.", "Idioma",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefrescarIdioma();
    }

    private void RefrescarRuso()
    {
        var (a, g) = Rim.RussianState();
        var lbl = _pages.Controls.Find("pg_idioma", false).FirstOrDefault()
                         ?.Controls.Find("rusoBox", false).FirstOrDefault()
                         ?.Controls.Find("lblRuso", false).FirstOrDefault() as Label;
        if (lbl != null) lbl.Text = $"{a} activas · {g} en cuarentena";
    }

    private void ToggleRuso(bool activar)
    {
        var (a, g) = Rim.RussianState();
        if (activar && g == 0)
        {
            MessageBox.Show(this, "No hay carpetas en cuarentena.", "Idioma",
                MessageBoxButtons.OK, MessageBoxIcon.Information); return;
        }
        if (!activar && a == 0)
        {
            MessageBox.Show(this, "No hay carpetas activas.", "Idioma",
                MessageBoxButtons.OK, MessageBoxIcon.Information); return;
        }
        var msg = activar ? $"Restaurar {g} carpetas de idioma ruso?" : $"Mover {a} carpetas a cuarentena?";
        if (MessageBox.Show(this, msg + "\n\nNo se borra nada.", "Idioma",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            int n = Rim.SetRussian(activar);
            RefrescarRuso();
            MessageBox.Show(this, $"{(activar ? "Restauradas" : "Movidas")}: {n}.", "Idioma",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Error: " + ex.Message, "Idioma", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

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

    private async void BuscarWorkshop()
    {
        var q = _txtUrl.Text.Trim();
        if (q.Length == 0) { SetOut("Escribe un nombre, un enlace o un ID para buscar."); return; }

        var key = Rim.Cfg.SteamApiKey?.Trim() ?? "";
        if (key.Length != 32)
        {
            SetOut("No hay una clave de Steam válida guardada.\n\n" +
                   "Pégala en el campo de abajo y pulsa «Guardar clave».\n" +
                   "Se obtiene gratis en steamcommunity.com/dev/apikey");
            return;
        }

        // si es un enlace o ID, extraer el numero
        var m = System.Text.RegularExpressions.Regex.Match(q, @"(\d{6,})");
        var consulta = m.Success ? m.Groups[1].Value : q;

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
        var ids = new List<string>();
        foreach (ListViewItem it in _lvRes.SelectedItems)
            if (it.Tag is string s && s.Length >= 6) ids.Add(s);

        if (ids.Count == 0)
        {
            var m = System.Text.RegularExpressions.Regex.Match(_txtUrl.Text, @"(\d{6,})");
            if (m.Success) ids.Add(m.Groups[1].Value);
        }
        if (ids.Count == 0) { SetOut("Selecciona un mod de la lista o pega su ID."); return; }

        if (MessageBox.Show(this,
            $"Se descargará con SteamCMD y se instalará en la carpeta de mods.\n" +
            $"Si el mod ya existe, se guarda copia antes de sustituirlo.\n\n" +
            $"Mods: {ids.Count}\n\n¿Continuar?",
            "Descargar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        SetOut("Descargando con SteamCMD… puede tardar varios minutos.");
        var (ok, log) = await Task.Run(() => Workshop.Descargar(ids));
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

    /// <summary>Busca mods desactualizados comparando fechas con el Workshop.</summary>
    private async void BuscarActualizaciones()
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
            _lblKeyState.Text = "Sin clave configurada · Workshop desactivado";
            _lblKeyState.ForeColor = Theme.Red;
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
