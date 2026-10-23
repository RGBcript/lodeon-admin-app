using System.Diagnostics;
using System.Text;
namespace LodeonAdmin;

/// <summary>Pagina de idioma: inventario, enlaces de traduccion y cuarentena.</summary>
public partial class MainForm
{
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

}
