using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SpotifyTB;

/// <summary>
/// Janela sem borda, sempre no topo, posicionada sobre a barra de tarefas
/// (ao lado da bandeja do sistema). Mostra o Spotify o tempo todo,
/// sem precisar abrir uma janela separada.
///
/// O desenho usa transparência por pixel (UpdateLayeredWindow), mantendo o fundo transparente
/// e aplicando apenas contraste suave ao conteúdo.
/// </summary>
internal sealed class TaskbarWidget : Form
{
    // ---------- Win32 ----------
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDc);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx, cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const int WsExToolWindow = 0x00000080;     // não aparece no Alt+Tab
    private const int WsExNoActivate = 0x08000000;     // não rouba o foco
    private const int WsExLayered = 0x00080000;        // transparência por pixel
    private const int UlwAlpha = 2;

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "SpotifyTB";

    // ---------- Estado ----------
    private readonly Settings _settings = Settings.Load();
    private readonly SpotifyService _spotify = new();
    private readonly ToolTip _tip = new();

    private readonly System.Windows.Forms.Timer _posTimer = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _mediaTimer = new() { Interval = 2000 };

    private readonly Font _fBold = new("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _fSmall = new("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel);
    private readonly Font _fVol = new("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _fNote = new("Segoe UI Symbol", 12f);

    private readonly StringFormat _fmt = MakeFormat(StringAlignment.Near);
    private readonly StringFormat _fmtCenter = MakeFormat(StringAlignment.Center);

    private Bitmap? _measureBmp;
    private Graphics? _mg;
    private int _mgDpi;

    private MediaInfo _media = MediaInfo.None;
    private bool _mediaBusy;

    private Color _back = Color.FromArgb(32, 32, 32);
    private bool _rendering;
    private int _renderW, _renderH;

    // Layout calculado
    private int _barHeight = 48;
    private int _contentWidth = 220;
    private int _lastDpi;
    private int _boldH = 16, _smallH = 14;
    private string _titleText = "";
    private string _artistText = "";
    private Rectangle _coverR, _titleR, _artistR;
    private readonly Rectangle[] _btnR = new Rectangle[3];
    private int _hover = -1;
    private float _volume = 1f;
    private bool _volumeMuted;
    private Rectangle _volumeIconR;
    private Rectangle _volumeBarR;
    private bool _volumeDragging;
    private Rectangle _seekR;
    private bool _seekDragging;

    // Arrastar com Ctrl
    private bool _dragging;
    private int _dragStartX, _dragStartOffset;

    public TaskbarWidget()
    {
        Text = "SpotifyTB";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Size = new Size(_contentWidth, _barHeight);
        ContextMenuStrip = BuildMenu();

        _posTimer.Tick += (_, _) => Reposition();
        _mediaTimer.Tick += async (_, _) => await RefreshMediaAsync();

        Shown += async (_, _) =>
        {
            ApplyTheme();
            BuildLayout();
            Reposition();
            _posTimer.Start();
            _mediaTimer.Start();
            await RefreshMediaAsync();
        };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExNoActivate | WsExLayered;
            return cp;
        }
    }

    // Todo Invalidate() vira um novo desenho da janela por pixel.
    protected override void OnInvalidated(InvalidateEventArgs e)
    {
        base.OnInvalidated(e);
        Render();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _posTimer.Dispose();
        _mediaTimer.Dispose();
        _tip.Dispose();
        _fmt.Dispose();
        _fmtCenter.Dispose();
        _mg?.Dispose();
        _measureBmp?.Dispose();
        base.OnFormClosed(e);
    }

    private static StringFormat MakeFormat(StringAlignment horizontal)
    {
        var f = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = horizontal,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.LineLimit
        };
        return f;
    }

    // ---------- Menu (botão direito) ----------
    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var right = new ToolStripMenuItem("Posição: ao lado da bandeja");
        right.Click += (_, _) => { _settings.Anchor = "right"; _settings.OffsetX = 0; _settings.Save(); Reposition(); };

        var left = new ToolStripMenuItem("Posição: canto esquerdo");
        left.Click += (_, _) => { _settings.Anchor = "left"; _settings.OffsetX = 0; _settings.Save(); Reposition(); };

        var moveLeft = new ToolStripMenuItem("Mover 20 px para a esquerda");
        moveLeft.Click += (_, _) => Nudge(-20);

        var moveRight = new ToolStripMenuItem("Mover 20 px para a direita");
        moveRight.Click += (_, _) => Nudge(20);

        var tip = new ToolStripMenuItem("Dica: Ctrl + arrastar move o widget") { Enabled = false };

        var color = new ToolStripMenuItem("Cor de fundo / película...");
        color.Click += (_, _) => PickColor();

        var autoColor = new ToolStripMenuItem("Cor de fundo automática");
        autoColor.Click += (_, _) => { _settings.BackColorArgb = null; _settings.Save(); ApplyTheme(); };

        var borderColor = new ToolStripMenuItem("Cor da borda...");
        borderColor.Click += (_, _) => PickBorderColor();

        var noBorder = new ToolStripMenuItem("Sem borda")
        {
            Checked = _settings.BorderColorArgb is null,
            CheckOnClick = true
        };
        noBorder.Click += (_, _) =>
        {
            _settings.BorderColorArgb = noBorder.Checked ? null : _settings.BorderColorArgb;
            _settings.Save();
            ApplyTheme();
        };

        var contrast = new ToolStripMenuItem("Realçar texto e botões")
        {
            Checked = _settings.ContrastBoost,
            CheckOnClick = true
        };
        contrast.CheckedChanged += (_, _) =>
        {
            _settings.ContrastBoost = contrast.Checked;
            _settings.Save();
            Invalidate();
        };

        var refresh = new ToolStripMenuItem("Atualizar agora");
        refresh.Click += async (_, _) => { await RefreshMediaAsync(); };

        var autostart = new ToolStripMenuItem("Iniciar com o Windows") { Checked = IsAutostart(), CheckOnClick = true };
        autostart.CheckedChanged += (_, _) => SetAutostart(autostart.Checked);

        var exit = new ToolStripMenuItem("Sair");
        exit.Click += (_, _) => Application.Exit();

        menu.Items.AddRange(new ToolStripItem[]
        {
            right, left, moveLeft, moveRight, tip,
            new ToolStripSeparator(),
            contrast,
            new ToolStripSeparator(),
            color, autoColor,
            borderColor, noBorder,
            new ToolStripSeparator(),
            refresh, autostart,
            new ToolStripSeparator(),
            exit
        });
        return menu;
    }

    private void Nudge(int dx)
    {
        _settings.OffsetX += dx;
        _settings.Save();
        Reposition();
    }

    private void PickColor()
    {
        using var dlg = new ColorDialog { Color = _settings.BackColorArgb is { } a ? Color.FromArgb(a) : _back, FullOpen = true };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _settings.BackColorArgb = dlg.Color.ToArgb();
        _settings.Save();
        ApplyTheme();
        Reposition();
    }

    // ---------- Cores / tema ----------
    private bool Dark => _back.GetBrightness() < 0.5f;
    private bool TextLight => true;

    private Color Fg => Color.White;
    private Color Muted => Color.FromArgb(248, 248, 250);
    private Color Track => Color.FromArgb(90, 255, 255, 255);

    private void ApplyTheme(bool force = true)
    {
        Color c = _settings.BackColorArgb is { } argb
            ? Color.FromArgb(argb)
            : (IsLightTaskbar() ? Color.FromArgb(243, 243, 243) : Color.FromArgb(32, 32, 32));

        if (!force && c.ToArgb() == _back.ToArgb()) return;
        _back = c;
        Invalidate();
    }

    private void PickBorderColor()
    {
        var initial = _settings.BorderColorArgb is { } argb
            ? Color.FromArgb(argb)
            : Fg;
        using var dlg = new ColorDialog { Color = initial, FullOpen = true };
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _settings.BorderColorArgb = dlg.Color.ToArgb();
        _settings.Save();
        ApplyTheme();
    }

    private static bool IsLightTaskbar()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch
        {
            return false;
        }
    }

    // ---------- Posicionamento sobre a barra de tarefas ----------
    private void HideWidget()
    {
        if (Visible) Hide();
    }

    private static int Px(float value, float scale) => (int)Math.Round(value * scale);

    /// <summary>Retângulo arredondado interno usado pelo efeito de contraste suave.</summary>
    private Rectangle Capsule(int w, int h)
    {
        float s = DeviceDpi / 96f;
        int ix = Px(2, s), iy = Px(3, s);
        return new Rectangle(ix, iy, Math.Max(1, w - 2 * ix), Math.Max(1, h - 2 * iy));
    }

    private void Reposition()
    {
        if (!IsHandleCreated) return;
        ApplyTheme(force: false);

        var tray = FindWindow("Shell_TrayWnd", null);
        if (tray == IntPtr.Zero || !GetWindowRect(tray, out var r))
        {
            HideWidget();
            return;
        }

        var tb = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
        var screen = Screen.FromRectangle(tb).Bounds;

        bool horizontal = tb.Width > tb.Height;
        bool taskbarHidden = Rectangle.Intersect(tb, screen).Height < tb.Height - 4;   // ocultar automaticamente
        bool fullscreen = SHQueryUserNotificationState(out int state) == 0 && state is 2 or 3 or 4;

        if (!horizontal || taskbarHidden || fullscreen || tb.Height < 20)
        {
            HideWidget();
            return;
        }

        if (_barHeight != tb.Height || _lastDpi != DeviceDpi)
        {
            _barHeight = tb.Height;
            BuildLayout();
        }

        float s = DeviceDpi / 96f;
        int x;
        if (_settings.Anchor == "left")
        {
            x = tb.Left + (int)(12 * s) + _settings.OffsetX;
        }
        else
        {
            int rightEdge;
            var notify = FindWindowEx(tray, IntPtr.Zero, "TrayNotifyWnd", null);
            if (notify != IntPtr.Zero && GetWindowRect(notify, out var n) && n.Right > n.Left)
                rightEdge = n.Left - (int)(8 * s);
            else
                rightEdge = tb.Right - (int)(280 * s);

            x = rightEdge - _contentWidth + _settings.OffsetX;
        }
        x = Math.Clamp(x, tb.Left, Math.Max(tb.Left, tb.Right - _contentWidth));

        if (!Visible) Show();

        // Reafirma "sempre no topo" (a própria barra também é topmost e sobe ao ser clicada).
        SetWindowPos(Handle, HwndTopmost, x, tb.Top, _contentWidth, tb.Height, SwpNoActivate | SwpShowWindow);

        if (_renderW != _contentWidth || _renderH != tb.Height) Render();
    }

    // ---------- Dados ----------
    private async Task RunCommand(Func<Task> command)
    {
        try
        {
            await command();
            await Task.Delay(400);
            await RefreshMediaAsync();
        }
        catch { /* a sessão do Spotify pode ter fechado */ }
    }

    private async Task RefreshMediaAsync()
    {
        if (_mediaBusy) return;
        _mediaBusy = true;
        try
        {
            var m = await _spotify.GetAsync();
            var prev = _media;
            _media = m;
            _volume = m.Volume;
            _volumeMuted = m.Muted;

            bool changed = prev.Available != m.Available
                || prev.Playing != m.Playing
                || prev.Duration != m.Duration
                || prev.Title != m.Title
                || prev.Artist != m.Artist
                || !ReferenceEquals(prev.Art, m.Art);

            if (changed)
            {
                BuildLayout();
                Reposition();
            }
            else if (m.Available)
            {
                Invalidate();   // atualiza a barra de progresso e o volume
            }
        }
        catch
        {
            _media = MediaInfo.None;
            BuildLayout();
            Reposition();
        }
        finally
        {
            _mediaBusy = false;
        }
    }

    // ---------- Layout ----------
    private void EnsureMeasureGraphics()
    {
        if (_mg is not null && _mgDpi == DeviceDpi) return;
        _mg?.Dispose();
        _measureBmp?.Dispose();
        _measureBmp = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        _measureBmp.SetResolution(DeviceDpi, DeviceDpi);
        _mg = Graphics.FromImage(_measureBmp);
        _mg.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        _mgDpi = DeviceDpi;
    }

    private Size Measure(string text, Font font)
    {
        EnsureMeasureGraphics();
        var size = TextRenderer.MeasureText(text, font,
            new Size(10000, 10000),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return new Size(size.Width + 2, size.Height);
    }

    private static string ShortName(string name) => name.Length > 12 ? name[..11] + "…" : name;

    private static string FormatTime(TimeSpan time)
        => time.Hours > 0
            ? time.ToString(@"h\:mm\:ss")
            : time.ToString(@"m\:ss");

    private void BuildLayout()
    {
        _lastDpi = DeviceDpi;
        float s = DeviceDpi / 96f;
        int h = _barHeight;
        int pad = (int)(8 * s);
        int gap = (int)(8 * s);
        int devGap = (int)(12 * s);

        _boldH = Measure("Ag", _fBold).Height;
        _smallH = Measure("Ag", _fSmall).Height;

        // Capa
        int cs = (int)(h * 0.68);
        _coverR = new Rectangle(pad, (h - cs) / 2, cs, cs);
        int right = _coverR.Right;
        int x = right + gap;

        // Título e artista
        _titleText = _media.Available
            ? (string.IsNullOrEmpty(_media.Title) ? "Spotify" : _media.Title)
            : "Spotify fechado";
        _artistText = _media.Available ? _media.Artist : "Abra o app do Spotify";

        int maxText = (int)(150 * s);
        int tw = Math.Min(maxText, Measure(_titleText, _fBold).Width);
        int aw = Math.Min(maxText, Measure(_artistText, _fSmall).Width);
        int textW = Math.Max(Math.Max(tw, aw), (int)(75 * s));
        int top = (h - (_boldH + _smallH)) / 2;
        _titleR = new Rectangle(x, top, textW, _boldH);
        _artistR = new Rectangle(x, top + _boldH, textW, _smallH);
        right = x + textW;
        x = right + gap;

        _seekR = Rectangle.Empty;
        if (_media.Available && _media.Duration.TotalSeconds > 0)
        {
            int seekHeight = (int)(4 * s);
            int seekY = Math.Min(h - seekHeight - (int)(3 * s), top + _boldH + _smallH);
            _seekR = new Rectangle(_titleR.X, seekY, textW, seekHeight);
        }

        // Botões anterior / play-pause / próxima
        if (_media.Available)
        {
            int bs = (int)(26 * s);
            int bg = (int)(2 * s);
            int by = (h - bs) / 2;
            for (int i = 0; i < 3; i++)
                _btnR[i] = new Rectangle(x + i * (bs + bg), by, bs, bs);
            right = _btnR[2].Right;
            x = right + gap;
        }
        else
        {
            for (int i = 0; i < 3; i++) _btnR[i] = Rectangle.Empty;
        }

        _volumeIconR = Rectangle.Empty;
        _volumeBarR = Rectangle.Empty;
        if (_media.Available)
        {
            int vs = (int)(28 * s);
            int by = (h - vs) / 2;
            _volumeIconR = new Rectangle(right + gap, by, vs, vs);
            right = _volumeIconR.Right;
            x = right + (int)(4 * s);
            int vw = (int)(36 * s);
            _volumeBarR = new Rectangle(x, by + (int)(9 * s), vw, (int)(6 * s));
            right = _volumeBarR.Right;
            x = right + gap;
        }

        _contentWidth = right + pad + (int)(2 * s);

        // Tooltip com o texto completo
        var lines = new List<string>();
        if (_media.Available)
        {
            lines.Add($"{_artistText} - {_titleText}");
            if (_media.Duration.TotalSeconds > 0)
                lines.Add($"{FormatTime(_media.Position)} / {FormatTime(_media.Duration)}");
        }
        _tip.SetToolTip(this, lines.Count > 0 ? string.Join("\n", lines) : "SpotifyTB");

        Invalidate();
    }

    // ---------- Desenho ----------
    private static GraphicsPath RoundRect(Rectangle r, int radius)
    {
        int d = Math.Max(2, radius * 2);
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Desenha o widget numa imagem com canal alfa e a entrega ao Windows.</summary>
    private void Render()
    {
        if (_rendering || !IsHandleCreated || _contentWidth <= 0 || _barHeight <= 0) return;
        _rendering = true;
        IntPtr screenDc = IntPtr.Zero, memDc = IntPtr.Zero, hBitmap = IntPtr.Zero, oldObj = IntPtr.Zero;
        try
        {
            int w = _contentWidth, h = _barHeight;
            using var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            bmp.SetResolution(DeviceDpi, DeviceDpi);
            using (var g = Graphics.FromImage(bmp))
                Draw(g, w, h);

            screenDc = GetDC(IntPtr.Zero);
            memDc = CreateCompatibleDC(screenDc);
            hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            oldObj = SelectObject(memDc, hBitmap);

            var size = new SIZE { cx = w, cy = h };
            var src = new POINT();
            var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, screenDc, IntPtr.Zero, ref size, memDc, ref src, 0, ref blend, UlwAlpha);

            _renderW = w;
            _renderH = h;
        }
        catch
        {
            // um quadro falho não deve derrubar o app
        }
        finally
        {
            if (memDc != IntPtr.Zero && oldObj != IntPtr.Zero) SelectObject(memDc, oldObj);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
            _rendering = false;
        }
    }

    /// <summary>Texto com sombra suave para permanecer legível sobre fundos transparentes.</summary>
    private void DrawText(Graphics g, string text, Font font, Rectangle rect, Color color, bool center = false)
    {
        const TextFormatFlags baseFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding
                                          | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
        var flags = baseFlags | (center
            ? TextFormatFlags.HorizontalCenter
            : TextFormatFlags.Left);

        if (_settings.ContrastBoost)
        {
            foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            {
                var shadowRect = new Rectangle(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
                TextRenderer.DrawText(g, text, font, shadowRect, Color.FromArgb(210, 0, 0, 0), flags);
            }

            TextRenderer.DrawText(g, text, font,
                new Rectangle(rect.X + 1, rect.Y + 1, rect.Width, rect.Height),
                Color.FromArgb(220, 0, 0, 0), flags);
        }

        TextRenderer.DrawText(g, text, font, rect, color, flags);
    }

    // kind: 0 = anterior, 1 = play, 2 = pause, 3 = próxima
    private static void AddGlyph(GraphicsPath p, int kind, Rectangle r, float s, float dx, float dy)
    {
        float cx = r.X + r.Width / 2f + dx, cy = r.Y + r.Height / 2f + dy;
        float a = 5f * s;
        switch (kind)
        {
            case 0:
                p.AddRectangle(new RectangleF(cx - a, cy - a, 2f * s, 2f * a));
                p.AddPolygon(new[]
                {
                    new PointF(cx + a, cy - a), new PointF(cx + a, cy + a), new PointF(cx - a + 2.5f * s, cy)
                });
                break;
            case 1:
                p.AddPolygon(new[]
                {
                    new PointF(cx - a * 0.6f, cy - a * 1.1f), new PointF(cx - a * 0.6f, cy + a * 1.1f), new PointF(cx + a, cy)
                });
                break;
            case 2:
                p.AddRectangle(new RectangleF(cx - 3.8f * s, cy - a, 2.8f * s, 2f * a));
                p.AddRectangle(new RectangleF(cx + 1.0f * s, cy - a, 2.8f * s, 2f * a));
                break;
            default:
                p.AddRectangle(new RectangleF(cx + a - 2f * s, cy - a, 2f * s, 2f * a));
                p.AddPolygon(new[]
                {
                    new PointF(cx - a, cy - a), new PointF(cx - a, cy + a), new PointF(cx + a - 2.5f * s, cy)
                });
                break;
        }
    }

    private void DrawGlyph(Graphics g, int kind, Rectangle r, Color color, float s)
    {
        if (_settings.ContrastBoost)
        {
            using var shadowPath = new GraphicsPath();
            AddGlyph(shadowPath, kind, r, s, 1f, 1f);
            using var shadow = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            g.FillPath(shadow, shadowPath);
        }
        using var path = new GraphicsPath();
        AddGlyph(path, kind, r, s, 0f, 0f);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    private void Draw(Graphics g, int w, int h)
    {
        float s = DeviceDpi / 96f;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var cap = Capsule(w, h);
        int radius = Px(7, s);

        g.Clear(Color.FromArgb(0, 0, 0, 0));

        if (_settings.ContrastBoost)
        {
            using var capsule = RoundRect(cap, radius);
            using var scrim = new SolidBrush(Color.FromArgb(58, 0, 0, 0));
            g.FillPath(scrim, capsule);
        }

        if (_settings.BorderColorArgb is { } borderArgb)
        {
            using var border = new Pen(Color.FromArgb(borderArgb), Math.Max(1, (int)s));
            using var outline = RoundRect(new Rectangle(0, 0, w - 1, h - 1), (int)(6 * s));
            g.DrawPath(border, outline);
        }

        // Capa do álbum
        using (var clip = RoundRect(_coverR, (int)(4 * s)))
        {
            if (_media.Available && _media.Art is { } art)
            {
                var state = g.Save();
                g.SetClip(clip, CombineMode.Replace);
                g.DrawImage(art, _coverR);
                g.Restore(state);
            }
            else
            {
                using var b = new SolidBrush(Track);
                g.FillPath(b, clip);
                DrawText(g, "♪", _fNote, _coverR, Muted, center: true);
            }
        }

        // Título e artista
        DrawText(g, _titleText, _fBold, _titleR, _media.Available ? Fg : Muted);
        DrawText(g, _artistText, _fSmall, _artistR, Muted);

        // Progresso da música
        if (!_seekR.IsEmpty)
        {
            using var seekBack = new SolidBrush(Color.FromArgb(110, 255, 255, 255));
            using var seekPath = RoundRect(_seekR, (int)(2 * s));
            g.FillPath(seekBack, seekPath);

            double progress = _media.Duration.TotalMilliseconds > 0
                ? Math.Clamp(_media.Progress, 0d, 1d)
                : 0d;
            int seekFillWidth = (int)(_seekR.Width * progress);
            if (seekFillWidth > 0)
            {
                var fill = new Rectangle(_seekR.X, _seekR.Y, seekFillWidth, _seekR.Height);
                using var fillPath = RoundRect(fill, (int)(2 * s));
                using var fillBrush = new SolidBrush(Color.FromArgb(29, 185, 84));
                g.FillPath(fillBrush, fillPath);
            }
        }

        // Botões (ícones desenhados, sem depender de fonte)
        int[] kinds = { 0, _media.Playing ? 2 : 1, 3 };
        for (int i = 0; i < 3; i++)
        {
            if (_btnR[i].IsEmpty) continue;
            using (var path = RoundRect(_btnR[i], (int)(7 * s)))
            {
                if (_settings.ContrastBoost)
                {
                    using var hb = new SolidBrush(Color.FromArgb(_hover == i ? 110 : 64, 0, 0, 0));
                    g.FillPath(hb, path);
                    using var ring = new Pen(Color.FromArgb(_hover == i ? 110 : 55, 255, 255, 255), 1f);
                    g.DrawPath(ring, path);
                }
                else if (_hover == i)
                {
                    using var hb = new SolidBrush(TextLight ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0));
                    g.FillPath(hb, path);
                }
            }
            DrawGlyph(g, kinds[i], _btnR[i], Fg, s);
        }

        // Volume do Spotify
        if (!_volumeIconR.IsEmpty)
        {
            using (var iconPath = RoundRect(_volumeIconR, (int)(7 * s)))
            {
                if (_hover == 3)
                {
                    using var hb = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
                    g.FillPath(hb, iconPath);
                }
            }
            DrawText(g, _volumeMuted ? "×" : "VOL", _fVol, _volumeIconR, Fg, center: true);

            using var barBack = new SolidBrush(Color.FromArgb(110, 255, 255, 255));
            using var barPath = RoundRect(_volumeBarR, (int)(3 * s));
            g.FillPath(barBack, barPath);

            int fillWidth = (int)(_volumeBarR.Width * (_volumeMuted ? 0 : _volume));
            if (fillWidth > 0)
            {
                var fill = new Rectangle(_volumeBarR.X, _volumeBarR.Y, fillWidth, _volumeBarR.Height);
                using var fillPath = RoundRect(fill, (int)(3 * s));
                using var fillBrush = new SolidBrush(Color.FromArgb(76, 194, 108));
                g.FillPath(fillBrush, fillPath);
            }
        }

    }

    // ---------- Mouse ----------
    private int HitButton(Point p)
    {
        for (int i = 0; i < 3; i++)
            if (_btnR[i].Contains(p)) return i;
        if (_volumeIconR.Contains(p)) return 3;
        if (_volumeBarR.Contains(p)) return 4;
        if (_seekR.Contains(p)) return 5;
        return -1;
    }

    private void SetVolumeFromPoint(int x)
    {
        if (_volumeBarR.Width <= 0) return;
        _volume = Math.Clamp((x - _volumeBarR.X) / (float)_volumeBarR.Width, 0f, 1f);
        _volumeMuted = false;
        _spotify.SetVolume(_volume);
        Invalidate();
    }

    private async void SeekFromPoint(int x)
    {
        if (_seekR.Width <= 0 || _media.Duration.TotalSeconds <= 0) return;
        double progress = Math.Clamp((x - _seekR.X) / (double)_seekR.Width, 0d, 1d);
        var position = TimeSpan.FromTicks((long)(_media.Duration.Ticks * progress));
        await _spotify.SeekAsync(position);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging)
        {
            _settings.OffsetX = _dragStartOffset + Cursor.Position.X - _dragStartX;
            Reposition();
            return;
        }

        if (_volumeDragging)
        {
            SetVolumeFromPoint(e.Location.X);
            return;
        }

        if (_seekDragging)
        {
            if (_seekR.Width > 0)
            {
                double progress = Math.Clamp((e.Location.X - _seekR.X) / (double)_seekR.Width, 0d, 1d);
                var position = TimeSpan.FromTicks((long)(_media.Duration.Ticks * progress));
                _ = _spotify.SeekAsync(position);
            }
            return;
        }

        int h = HitButton(e.Location);
        if (h == _hover) return;
        _hover = h;
        Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover == -1) return;
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_volumeIconR.Contains(e.Location) && !_volumeBarR.Contains(e.Location)
            && !ClientRectangle.Contains(e.Location)) return;

        _volume = Math.Clamp(_volume + (e.Delta > 0 ? 0.05f : -0.05f), 0f, 1f);
        _volumeMuted = false;
        _spotify.SetVolume(_volume);
        Invalidate();
    }

    protected override async void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Right)
        {
            ContextMenuStrip?.Show(Cursor.Position);
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        if ((ModifierKeys & Keys.Control) != 0)
        {
            _dragging = true;
            _dragStartX = Cursor.Position.X;
            _dragStartOffset = _settings.OffsetX;
            return;
        }

        switch (HitButton(e.Location))
        {
            case 0: await RunCommand(_spotify.PreviousAsync); break;
            case 1: await RunCommand(_spotify.PlayPauseAsync); break;
            case 2: await RunCommand(_spotify.NextAsync); break;
            case 3:
                _spotify.ToggleMute();
                _volumeMuted = !_volumeMuted;
                Invalidate();
                break;
            case 4:
                _volumeDragging = true;
                SetVolumeFromPoint(e.Location.X);
                break;
            case 5:
                _seekDragging = true;
                SeekFromPoint(e.Location.X);
                break;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_volumeDragging)
        {
            _volumeDragging = false;
            return;
        }
        if (_seekDragging)
        {
            SeekFromPoint(e.Location.X);
            _seekDragging = false;
            return;
        }
        if (!_dragging) return;
        _dragging = false;
        _settings.Save();
    }

    // ---------- Iniciar com o Windows ----------
    private static bool IsAutostart()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) is not null;
    }

    private static void SetAutostart(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;
        if (enable) key.SetValue(AppName, $"\"{Environment.ProcessPath ?? Application.ExecutablePath}\"");
        else key.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
