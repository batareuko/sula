using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AionMeter.App.Services;

namespace AionMeter.App.Windows;

/// <summary>
/// The arrow itself, drawn with <see cref="DrawingContext"/> (the C# equivalent of Python's QPainter): a green 3-D-ish
/// arrow — soft glow, black outline, gradient fill — that points from the character to the cube. It lives in its own
/// full-screen transparent window and never takes input.
/// </summary>
public sealed class CubeArrowView : FrameworkElement
{
    private double _angle;
    private Point _anchor;
    private double _length = 120;
    private double _thickness = 22;
    private Color _color = Color.FromRgb(0x39, 0xFF, 0x14);
    private bool _visible;

    public bool Visible => _visible;

    public void Show(double angle, Point anchor, double length, double thickness, Color color)
    {
        _angle = angle;
        _anchor = anchor;
        _length = Math.Max(12, length);
        _thickness = Math.Max(4, thickness);
        _color = color;
        _visible = true;
        InvalidateVisual();
    }

    public void Hide()
    {
        if (!_visible) return;
        _visible = false;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (!_visible || ActualWidth <= 0 || ActualHeight <= 0) return;

        // The arrow is built pointing right around the origin, then rotated by the cube's bearing and moved to its anchor.
        var group = new TransformGroup();
        group.Children.Add(new RotateTransform(_angle));
        group.Children.Add(new TranslateTransform(_anchor.X, _anchor.Y));
        var geom = ArrowGeometry(_length, _thickness);
        geom.Transform = group;
        geom.Freeze();

        var (r, g, b) = (_color.R, _color.G, _color.B);

        // Soft glow: widening outline rings in the arrow's colour, fading out.
        for (var i = 3; i >= 1; i--)
        {
            var alpha = (byte)Math.Max(0, 30 - i * 9);
            var glow = new Pen(new SolidColorBrush(Color.FromArgb(alpha, r, g, b)), _thickness + i * 8)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            glow.Freeze();
            dc.DrawGeometry(null, glow, geom);
        }

        // Black outline, then the gradient fill (lighter at the tip for a 3-D look).
        var outline = new Pen(Brushes.Black, Math.Max(1, _thickness * 0.16)) { LineJoin = PenLineJoin.Round };
        outline.Freeze();
        dc.DrawGeometry(null, outline, geom);

        var light = Color.FromRgb(Lighten(r), Lighten(g), Lighten(b));
        var fill = new LinearGradientBrush(light, _color, new Point(_length / 2, 0), new Point(-_length / 2, 0))
        {
            MappingMode = BrushMappingMode.Absolute,
            Transform = group,
        };
        fill.Freeze();
        dc.DrawGeometry(fill, null, geom);
    }

    /// <summary>An arrow pointing right, centred on the origin: triangle head + shaft + flat tail.</summary>
    private static StreamGeometry ArrowGeometry(double length, double thickness)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            var head = thickness * 1.6;
            var half = thickness / 2.0;
            var shaft = thickness * 0.45;
            var halfL = length / 2.0;
            c.BeginFigure(new Point(halfL, 0), isFilled: true, isClosed: true);
            c.LineTo(new Point(halfL - head, -half), isStroked: true, isSmoothJoin: false);
            c.LineTo(new Point(-halfL, -shaft), isStroked: true, isSmoothJoin: false);
            c.LineTo(new Point(-halfL, shaft), isStroked: true, isSmoothJoin: false);
            c.LineTo(new Point(halfL - head, half), isStroked: true, isSmoothJoin: false);
        }
        g.Freeze();
        return g;
    }

    private static byte Lighten(byte c) => (byte)(c + (255 - c) / 2);
}

/// <summary>
/// 1 HP: a full-screen, transparent, click-through window that shows the cube arrow above the game. The C# equivalent
/// of the Python <c>cube_arrow_overlay.py</c> QWidget: frameless, always on top, translucent and transparent to the
/// mouse and keyboard. When the cube disappears the arrow fades out after the configured delay.
/// </summary>
public sealed class CubeArrowOverlay : Window
{
    private readonly AppSettings _settings;
    private readonly CubeArrowView _view = new();
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    public CubeArrowOverlay(AppSettings settings)
    {
        _settings = settings;
        Title = "1 HP Cube";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Content = _view;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetExStyle(hwnd, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT);
        };

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            _view.Hide();
        };
    }

    /// <summary>
    /// Points the arrow at <paramref name="target"/> (its centre, relative to <paramref name="region"/>) or, when the
    /// cube is gone (null), starts the fade. The bearing is computed from the character — the centre of the screen — to
    /// the cube, exactly like the Python overlay.
    /// </summary>
    public void UpdateTarget(CubeTarget? target, ScreenRegion region)
    {
        if (region.IsEmpty) region = ScreenCapture.VirtualScreen();
        if (target is { } t)
        {
            _hideTimer.Stop();
            var cubeX = region.Left + t.X;
            var cubeY = region.Top + t.Y;
            var charX = region.Left + region.Width / 2.0;
            var charY = region.Top + region.Height / 2.0;
            var angle = Math.Atan2(cubeY - charY, cubeX - charX) * 180.0 / Math.PI;
            var anchorX = charX;
            var anchorY = region.Top + region.Height * Math.Clamp(_settings.CubeArrowYPercent, 1, 100) / 100.0;
            var (ax, ay) = ToLocal(anchorX, anchorY);
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            _view.Show(angle, new Point(ax, ay), _settings.CubeArrowLength / dpi, _settings.CubeArrowThickness / dpi,
                ParseColor(_settings.CubeArrowColor));
        }
        else if (_view.Visible)
        {
            _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(10, _settings.CubeHideDelayMs));
            _hideTimer.Stop();
            _hideTimer.Start();
        }
    }

    /// <summary>Physical screen pixels → the overlay's local DIPs (the window covers the whole virtual screen).</summary>
    private (double X, double Y) ToLocal(double physX, double physY)
    {
        var v = ScreenCapture.VirtualScreen();
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        return (SystemParameters.VirtualScreenLeft + (physX - v.Left) / dpi,
                SystemParameters.VirtualScreenTop + (physY - v.Top) / dpi);
    }

    private static Color ParseColor(string hex)
    {
        try
        {
            var s = hex.Trim().TrimStart('#');
            if (s.Length == 6 && uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
                return Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }
        catch (Exception)
        {
        }
        return Color.FromRgb(0x39, 0xFF, 0x14);
    }
}
