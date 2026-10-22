using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace LodeonAdmin;

/// <summary>
/// Sistema visual. Jerarquia por tres ejes: tamano (escala), color (contraste) y
/// espacio (agrupacion). Sin iconos ni emojis.
/// </summary>
public static class Theme
{
    // --- superficies (de mas oscura a mas clara)
    public static readonly Color Bg = Color.FromArgb(14, 16, 20);
    public static readonly Color Side = Color.FromArgb(10, 12, 15);
    public static readonly Color Card = Color.FromArgb(21, 24, 29);
    public static readonly Color CardHi = Color.FromArgb(28, 32, 38);
    public static readonly Color Line = Color.FromArgb(33, 37, 44);

    // --- texto (escala de contraste)
    public static readonly Color Text = Color.FromArgb(238, 240, 244);
    public static readonly Color Text2 = Color.FromArgb(168, 175, 185);
    public static readonly Color Text3 = Color.FromArgb(110, 118, 130);

    // --- acentos
    public static readonly Color Gold = Color.FromArgb(224, 190, 112);
    public static readonly Color Green = Color.FromArgb(94, 184, 132);
    public static readonly Color Red = Color.FromArgb(214, 106, 106);
    public static readonly Color Blue = Color.FromArgb(96, 146, 198);

    // --- escala tipografica
    public static Font Display() => new("Segoe UI", 20f, FontStyle.Bold, GraphicsUnit.Point);
    public static Font H1() => new("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Point);
    public static Font H2() => new("Segoe UI", 10.5f, FontStyle.Bold, GraphicsUnit.Point);
    public static Font Body() => new("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
    public static Font BodyStrong() => new("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
    public static Font Caption() => new("Segoe UI", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
    public static Font Micro() => new("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Point);
    public static Font Mono(float size = 8.75f) => new("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);

    /// <summary>Espaciado base: multiplos de 4.</summary>
    public const int S1 = 4, S2 = 8, S3 = 12, S4 = 16, S5 = 24, S6 = 32;

    public static GraphicsPath Round(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        if (d <= 0 || r.Width <= 0 || r.Height <= 0) { p.AddRectangle(r); return p; }
        if (d > r.Width) d = r.Width;
        if (d > r.Height) d = r.Height;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Rectangle r, int radius, Color c)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var p = Round(r, radius);
        using var b = new SolidBrush(c);
        g.FillPath(b, p);
    }

    public static void Stroke(Graphics g, Rectangle r, int radius, Color c, float w = 1f)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var p = Round(r, radius);
        using var pen = new Pen(c, w);
        g.DrawPath(pen, p);
    }

    /// <summary>Texto con una sola linea, recortado por elipsis.</summary>
    public static void Text1(Graphics g, string t, Font f, Rectangle r, Color c,
                             TextFormatFlags extra = TextFormatFlags.Left)
    {
        TextRenderer.DrawText(g, t, f, r, c, extra | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>Habilita las barras de scroll oscuras de Windows.</summary>
public static class DarkScroll
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr h, string? a, string? b);

    public static void Apply(Control c)
    {
        try
        {
            if (c.IsHandleCreated) SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
            else c.HandleCreated += (_, _) => { try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
        }
        catch { }
    }
}

/// <summary>Boton plano con transicion de color al pasar el raton.</summary>
public class FlatBtn : Button
{
    private float _hover;
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 16 };

    public Color Base { get; set; } = Theme.Card;
    public Color HoverColor { get; set; } = Theme.CardHi;
    public Color TextColor { get; set; } = Theme.Text;
    public int Radius { get; set; } = 5;

    public FlatBtn(string text, int x, int y, int w, int h)
    {
        Text = text;
        SetBounds(x, y, w, h);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.Bg;
        Font = Theme.Body();
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _anim.Tick += (_, _) => Step();
    }

    private void Step()
    {
        float target = _hoverTarget;
        _hover += (target - _hover) * 0.35f;
        if (Math.Abs(target - _hover) < 0.02f) { _hover = target; _anim.Stop(); }
        Invalidate();
    }

    private float _hoverTarget;
    protected override void OnMouseEnter(EventArgs e)
    {
        _hoverTarget = 1f; _anim.Start(); base.OnMouseEnter(e);
    }
    protected override void OnMouseLeave(EventArgs e)
    {
        _hoverTarget = 0f; _anim.Start(); base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Parent?.BackColor ?? Theme.Bg))
            g.FillRectangle(bg, ClientRectangle);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        var col = Enabled ? Blend(Base, HoverColor, _hover) : Color.FromArgb(24, 27, 32);
        Theme.FillRound(g, r, Radius, col);

        // borde sutil solo en reposo, para separar del fondo
        if (Enabled && _hover < 0.5f) Theme.Stroke(g, r, Radius, Theme.Line);

        Theme.Text1(g, Text, Font, r, Enabled ? TextColor : Theme.Text3,
            TextFormatFlags.HorizontalCenter);
    }

    private static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}

/// <summary>Item de navegacion: texto tenue en reposo, claro al seleccionar.</summary>
public class NavBtn : Button
{
    public bool Selected { get; set; }
    private float _hover;
    private float _hoverTarget;
    private readonly System.Windows.Forms.Timer _anim = new() { Interval = 16 };

    public NavBtn(string text, int x, int y, int w, int h)
    {
        Text = text;
        SetBounds(x, y, w, h);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Theme.Side;
        Font = Theme.Body();
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        _anim.Tick += (_, _) =>
        {
            _hover += (_hoverTarget - _hover) * 0.35f;
            if (Math.Abs(_hoverTarget - _hover) < 0.02f) { _hover = _hoverTarget; _anim.Stop(); }
            Invalidate();
        };
    }

    protected override void OnMouseEnter(EventArgs e) { _hoverTarget = 1f; _anim.Start(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hoverTarget = 0f; _anim.Start(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.Side))
            g.FillRectangle(bg, ClientRectangle);

        if (Selected)
        {
            using var acc = new SolidBrush(Theme.Gold);
            g.FillRectangle(acc, 0, 10, 2, Height - 20);
        }
        else if (_hover > 0.01f)
        {
            using var b = new SolidBrush(Color.FromArgb((int)(18 * _hover), 21, 26));
            g.FillRectangle(b, 0, 2, Width, Height - 4);
        }

        var col = Selected ? Theme.Text
                : _hover > 0.5f ? Color.FromArgb(200, 206, 214)
                : Theme.Text3;
        Theme.Text1(g, Text, Selected ? Theme.BodyStrong() : Theme.Body(),
            new Rectangle(22, 0, Width - 30, Height), col);
    }
}

/// <summary>Tarjeta de metrica: etiqueta pequena, valor grande, color de estado.</summary>
public class StatCard : Panel
{
    public string Caption { get; set; } = "";
    public string Value { get; set; } = "";
    public string Hint { get; set; } = "";
    public Color Accent { get; set; } = Theme.Gold;

    public StatCard()
    {
        BackColor = Theme.Bg;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.Bg))
            g.FillRectangle(bg, ClientRectangle);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        Theme.FillRound(g, r, 6, Theme.Card);

        // barra de estado a la izquierda
        using (var acc = new SolidBrush(Accent))
            g.FillRectangle(acc, 0, 12, 2, Height - 24);

        Theme.Text1(g, Caption.ToUpperInvariant(), Theme.Micro(),
            new Rectangle(14, 10, Width - 24, 12), Theme.Text3);

        var f = Value.Length > 18 ? Theme.H2() : Theme.Display();
        Theme.Text1(g, Value, f, new Rectangle(14, 24, Width - 24, Height - 44), Theme.Text);

        if (!string.IsNullOrEmpty(Hint))
            Theme.Text1(g, Hint, Theme.Caption(),
                new Rectangle(14, Height - 22, Width - 24, 14), Theme.Text3);
    }
}

/// <summary>Encabezado de pagina: titulo grande y una linea de explicacion.</summary>
public class PageHeader : Panel
{
    public string Heading { get; set; } = "";
    public string Detail { get; set; } = "";

    public PageHeader(string heading, string detail)
    {
        Heading = heading;
        Detail = detail;
        BackColor = Theme.Bg;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new SolidBrush(Theme.Bg))
            g.FillRectangle(bg, ClientRectangle);

        Theme.Text1(g, Heading, Theme.H1(), new Rectangle(0, 0, Width, 24), Theme.Text);
        if (!string.IsNullOrEmpty(Detail))
            Theme.Text1(g, Detail, Theme.Caption(), new Rectangle(0, 22, Width, 16), Theme.Text3);
    }
}

/// <summary>Titulo de bloque dentro de una pagina.</summary>
public class SectionLabel : Panel
{
    public string Heading { get; set; } = "";

    public SectionLabel(string text)
    {
        Heading = text;
        BackColor = Theme.Bg;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var bg = new SolidBrush(Theme.Bg))
            g.FillRectangle(bg, ClientRectangle);
        Theme.Text1(g, Heading.ToUpperInvariant(), Theme.Micro(),
            new Rectangle(0, 0, Width, 12), Theme.Text3);
    }
}

/// <summary>Fila de accion: boton a la izquierda, explicacion breve al lado.</summary>
public class ActionRow : Panel
{
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public string ButtonText { get; set; } = "";
    public Color ButtonColor { get; set; } = Theme.Card;
    public Color ButtonTextColor { get; set; } = Theme.Text;
    public event Action? Invoke;

    private bool _hover;

    public ActionRow(string title, string detail, string buttonText)
    {
        Title = title;
        Detail = detail;
        ButtonText = buttonText;
        BackColor = Theme.Bg;
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e) { Invoke?.Invoke(); base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(Theme.Bg))
            g.FillRectangle(bg, ClientRectangle);

        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        Theme.FillRound(g, r, 6, _hover ? Theme.CardHi : Theme.Card);

        // boton a la derecha
        int bw = Math.Min(190, Math.Max(120, Width / 4));
        var br = new Rectangle(Width - bw - 12, (Height - 28) / 2, bw, 28);
        Theme.FillRound(g, br, 4, _hover ? ControlPaint.Light(ButtonColor, 0.08f) : ButtonColor);
        Theme.Text1(g, ButtonText, Theme.Body(), br, ButtonTextColor, TextFormatFlags.HorizontalCenter);

        // texto
        int txtW = Width - bw - 40;
        Theme.Text1(g, Title, Theme.BodyStrong(), new Rectangle(14, 8, txtW, 18), Theme.Text);
        if (!string.IsNullOrEmpty(Detail))
            Theme.Text1(g, Detail, Theme.Caption(), new Rectangle(14, 26, txtW, 16), Theme.Text3);
    }
}
