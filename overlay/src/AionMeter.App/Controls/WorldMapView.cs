using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AionMeter.Core.OneHp;

namespace AionMeter.App.Controls;

/// <summary>
/// 1 HP: a world map — the game's map picture (from Aion2Data) with the chosen layers' places as dots, the main areas
/// named. Wheel zooms around the cursor, drag pans, hovering a dot names it.
/// </summary>
public sealed class WorldMapView : FrameworkElement
{
    private WorldMaps.Map? _map;
    private ImageSource? _image;
    private IReadOnlySet<string> _layers = new HashSet<string>();
    private Func<WorldMaps.Place, bool> _dim = _ => false;
    private double _zoom = 1;
    private Point _pan; // top-left of the map in pixels
    private Point? _dragFrom;
    private (string Layer, WorldMaps.Place Place, (double X, double Y) Pt)? _hover;
    private (double X, double Y)? _you;
    private bool _youExact;

    /// <summary>The game's icons by resource name (null while one downloads: a dot stands in).</summary>
    public Func<string, ImageSource?> Icon { get; set; } = _ => null;

    /// <summary>"You are here" in map units; null hides it. <paramref name="exact"/>: from your own move.</summary>
    public void SetYou((double X, double Y)? at, bool exact)
    {
        if (_you == at && _youExact == exact) return;
        _you = at;
        _youExact = exact;
        InvalidateVisual();
    }

    /// <summary>Centres the view on "you are here", zoomed in a little.</summary>
    public void CentreOnYou()
    {
        if (_you is not { } y || _map is null) return;
        _zoom = Math.Max(_zoom, 3);
        var p = (X: y.X * UnitsToPx, Y: y.Y * UnitsToPx);
        _pan = new Point(ActualWidth / 2 - p.X, ActualHeight / 2 - p.Y);
        Fit();
        InvalidateVisual();
    }

    public void Redraw() => InvalidateVisual();

    /// <summary>The mini-map: you in the middle at this zoom (1 = the whole map in the view).</summary>
    public void Follow((double X, double Y) at, double zoom)
    {
        if (_map is null) return;
        _zoom = Math.Clamp(zoom, 1, 40);
        var k = UnitsToPx;
        _pan = new Point(ActualWidth / 2 - at.X * k, ActualHeight / 2 - at.Y * k);
        InvalidateVisual(); // no clamping: you stay in the middle even at the map's edge
    }

    public double Zoom => _zoom;

    /// <summary>Small view (the mini-map): no scale bar, no area names, smaller icons.</summary>
    public bool Compact { get; set; }

    public WorldMapView()
    {
        ClipToBounds = true;
        Focusable = false;
        Cursor = Cursors.Hand;
    }

    /// <summary>Shows <paramref name="map"/> fitted to the view (a new map resets zoom and pan).</summary>
    public void SetMap(WorldMaps.Map map, ImageSource? image)
    {
        if (_map?.Slug != map.Slug)
        {
            _zoom = 1;
            _pan = default;
            _hover = null;
        }
        _map = map;
        _image = image;
        Fit();
        InvalidateVisual();
    }

    public void SetImage(ImageSource? image)
    {
        _image = image;
        InvalidateVisual();
    }

    /// <param name="dim">Places drawn small and grey (pets you already have).</param>
    public void SetLayers(IReadOnlySet<string> layers, Func<WorldMaps.Place, bool> dim)
    {
        _layers = layers;
        _dim = dim;
        _hover = null;
        InvalidateVisual();
    }

    /// <summary>Text of the place under the cursor (the window shows it below the map).</summary>
    public event Action<string?>? HoverChanged;

    private double Base => _map is null ? 1 : Math.Min(ActualWidth / _map.Width, ActualHeight / _map.Height);
    private double Scale => Base * _zoom; // picture pixels → screen
    private double UnitsToPx => _map is null ? 1 : Scale * _map.Width / WorldMaps.Units;

    private Point ToScreen((double X, double Y) p) => new(_pan.X + p.X * UnitsToPx, _pan.Y + p.Y * UnitsToPx);

    private void Fit()
    {
        if (_map is null || ActualWidth <= 0) return;
        var w = _map.Width * Scale;
        var h = _map.Height * Scale;
        // a smaller picture than the view stays centred; a bigger one may not leave a gap at its edges
        _pan.X = w <= ActualWidth ? (ActualWidth - w) / 2 : Math.Clamp(_pan.X, ActualWidth - w, 0);
        _pan.Y = h <= ActualHeight ? (ActualHeight - h) / 2 : Math.Clamp(_pan.Y, ActualHeight - h, 0);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Fit();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_map is null) return;
        var at = e.GetPosition(this);
        var before = _zoom;
        if (Compact) return; // the mini-map zooms itself (its window handles the wheel)
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.25 : 0.8), 1, 12);
        var k = _zoom / before;
        _pan = new Point(at.X - (at.X - _pan.X) * k, at.Y - (at.Y - _pan.Y) * k);
        Fit();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Compact) return; // the mini-map's window is dragged instead
        _dragFrom = e.GetPosition(this);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragFrom = null;
        ReleaseMouseCapture();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var at = e.GetPosition(this);
        if (_dragFrom is { } from)
        {
            _pan += at - from;
            _dragFrom = at;
            Fit();
            InvalidateVisual();
            return;
        }
        var hit = Nearest(at, 12);
        if (hit?.Pt != _hover?.Pt || hit?.Place != _hover?.Place)
        {
            _hover = hit;
            InvalidateVisual();
            HoverChanged?.Invoke(hit is { } h ? Describe(h.Place) : null);
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hover is null) return;
        _hover = null;
        InvalidateVisual();
        HoverChanged?.Invoke(null);
    }

    private static bool Dense(string key) => key.StartsWith("pets", StringComparison.Ordinal) || key is "gather" or "cubes";

    private static string Describe(WorldMaps.Place p) =>
        p.Name + (string.IsNullOrEmpty(p.Sub) ? "" : " · " + p.Sub) + (p.Points.Count > 1 ? $" · {p.Points.Count} ×" : "");

    private (string Layer, WorldMaps.Place Place, (double X, double Y) Pt)? Nearest(Point at, double within)
    {
        if (_map is null) return null;
        (string Layer, WorldMaps.Place Place, (double X, double Y) Pt)? best = null;
        var bestD = within * within;
        foreach (var (key, places) in Visible())
            foreach (var place in places)
                foreach (var pt in place.Points)
                {
                    var p = ToScreen(pt);
                    var d = (p.X - at.X) * (p.X - at.X) + (p.Y - at.Y) * (p.Y - at.Y);
                    if (d < bestD) { bestD = d; best = (key, place, pt); }
                }
        return best;
    }

    private IEnumerable<(string Key, IReadOnlyList<WorldMaps.Place> Places)> Visible()
    {
        if (_map is null) yield break;
        foreach (var key in WorldMaps.LayerOrder)
            if (_layers.Contains(key) && _map.Layers.TryGetValue(key, out var places))
                yield return (key, places);
    }

    private static readonly Typeface Face = new("Segoe UI");
    private static readonly Typeface Bold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Brush Paper = Frozen(Color.FromRgb(0x0A, 0x14, 0x1A));
    private static readonly Brush Grey = Frozen(Color.FromArgb(0xB0, 0x8A, 0x94, 0xA6));
    private static readonly Brush Shadow = Frozen(Color.FromArgb(0xC0, 0, 0, 0));
    private static readonly Pen Edge = FrozenPen(Color.FromArgb(0xE0, 0x0A, 0x0E, 0x16), 1);
    private static readonly Pen HoverRing = FrozenPen(Colors.White, 2);
    private static readonly Brush YouFill = Frozen(Color.FromRgb(0xFF, 0xD3, 0x4D));
    private static readonly Brush YouHalo = Frozen(Color.FromArgb(0x55, 0xFF, 0xD3, 0x4D));
    private static readonly Pen YouRing = FrozenPen(Color.FromRgb(0xFF, 0xD3, 0x4D), 2);
    private static readonly Pen YouGuess = DashedPen(Color.FromRgb(0xFF, 0xD3, 0x4D));

    private static Pen DashedPen(Color c)
    {
        var p = new Pen(Frozen(c), 2) { DashStyle = DashStyles.Dash };
        p.Freeze();
        return p;
    }

    /// <summary>The layer's colour (the same as on Aion2Data's map).</summary>
    public static Color LayerColor(string key) => key switch
    {
        "bosses" => Color.FromRgb(0xE5, 0x48, 0x4D),
        "dungeons" => Color.FromRgb(0xF0, 0x8A, 0x3C),
        "rifts" => Color.FromRgb(0x3F, 0xD0, 0xE0),
        "kibelisks" => Color.FromRgb(0x7A, 0xA7, 0xFF),
        "monoliths" => Color.FromRgb(0xE8, 0xE0, 0xA0),
        "gather" => Color.FromRgb(0x5F, 0xBF, 0x6A),
        "pets-fera" => Color.FromRgb(0xFF, 0x7A, 0x5C),
        "pets-natura" => Color.FromRgb(0x6C, 0xD8, 0x7A),
        "pets-varian" => Color.FromRgb(0x5C, 0xB4, 0xFF),
        "pets-cogni" => Color.FromRgb(0xC0, 0x8C, 0xFF),
        "cubes" => Color.FromRgb(0xF2, 0xB6, 0x32),
        _ => Colors.White,
    };

    private static readonly Dictionary<string, Brush> Fills = WorldMaps.LayerOrder.ToDictionary(k => k, k => Frozen(LayerColor(k)));

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Paper, null, new Rect(RenderSize));
        if (_map is null) return;
        var picture = new Rect(_pan, new Size(_map.Width * Scale, _map.Height * Scale));
        if (_image is not null) dc.DrawImage(_image, picture);
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // main areas by name: the biggest ones always, the smaller ones once zoomed in
        foreach (var a in _map.Areas)
        {
            if (Compact || (a.Rank > 1 && _zoom < 2.5)) continue;
            var text = new FormattedText(a.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, a.Rank == 1 ? Bold : Face,
                a.Rank == 1 ? 12 : 10.5, Brushes.White, dpi);
            var p = ToScreen((a.X, a.Y));
            var at = new Point(p.X - text.Width / 2, p.Y - text.Height / 2);
            dc.DrawText(new FormattedText(a.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, a.Rank == 1 ? Bold : Face,
                a.Rank == 1 ? 12 : 10.5, Shadow, dpi), at + new Vector(1, 1));
            dc.DrawText(text, at);
        }

        var r = Math.Clamp(2.5 + _zoom * 0.6, 3, 7);
        var size = Compact ? 18 : Math.Clamp(14 + _zoom * 3, 16, 30); // icon size, px
        // the many small spots (pets, gathering, cube spots) below, the few landmarks on top of them
        foreach (var (key, places) in Visible().OrderBy(l => Dense(l.Key) ? 0 : 1))
        {
            var fill = Fills[key];
            var dense = Dense(key);
            foreach (var place in places)
            {
                var dim = _dim(place);
                // the many spots of a dense layer are dots until zoomed in, so they do not bury the map
                var icon = dense && _zoom < (Compact ? 6 : 2) ? null : Icon(WorldMaps.IconOf(key, place));
                var s = dense ? size * 0.72 : size;
                if (dim) dc.PushOpacity(0.3);
                foreach (var pt in place.Points)
                {
                    var p = ToScreen(pt);
                    if (p.X < -20 || p.Y < -20 || p.X > ActualWidth + 20 || p.Y > ActualHeight + 20) continue;
                    if (icon is not null && key.StartsWith("pets", StringComparison.Ordinal))
                    {
                        // a pet soul: its family's colour around the soul crystal
                        dc.DrawEllipse(dim ? Grey : fill, Edge, p, s / 2, s / 2);
                        dc.DrawImage(icon, new Rect(p.X - s * 0.36, p.Y - s * 0.36, s * 0.72, s * 0.72));
                    }
                    else if (icon is not null) dc.DrawImage(icon, new Rect(p.X - s / 2, p.Y - s / 2, s, s));
                    else dc.DrawEllipse(dim ? Grey : fill, dense ? null : Edge, p, dense ? r * 0.6 : r, dense ? r * 0.6 : r);
                }
                if (dim) dc.Pop();
            }
        }

        if (_you is { } you)
        {
            // you: a gold dot in a ring; a dashed ring when the place is a guess (a monster you hit, the arrival)
            var p = ToScreen(you);
            dc.DrawEllipse(YouHalo, null, p, 14, 14);
            dc.DrawEllipse(YouFill, new Pen(Brushes.Black, 1.5), p, 6, 6);
            dc.DrawEllipse(null, _youExact ? YouRing : YouGuess, p, 11, 11);
        }

        if (_hover is { } h)
        {
            // every spot of the hovered place, and its name by the cursor's dot
            foreach (var pt in h.Place.Points)
                dc.DrawEllipse(null, HoverRing, ToScreen(pt), size * 0.6, size * 0.6);
            var p = ToScreen(h.Pt);
            var label = new FormattedText(h.Place.Name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Bold, 12, Brushes.White, dpi);
            var x = p.X + 10 + label.Width <= ActualWidth - 4 ? p.X + 10 : p.X - 10 - label.Width;
            dc.DrawRoundedRectangle(Shadow, null, new Rect(x - 4, p.Y - 10, label.Width + 8, label.Height + 2), 4, 4);
            dc.DrawText(label, new Point(x, p.Y - 9));
        }

        if (Compact) return;

        // scale bar
        var metresPerPx = _map.Metres / (_map.Width * Scale);
        var step = new[] { 50, 100, 200, 250, 500, 1000, 2000 }.LastOrDefault(m => m / metresPerPx <= 140, 50);
        var len = step / metresPerPx;
        var y = ActualHeight - 14;
        dc.DrawRectangle(Shadow, null, new Rect(8, y - 14, len + 50, 22));
        dc.DrawLine(new Pen(Brushes.White, 2), new Point(14, y), new Point(14 + len, y));
        dc.DrawText(new FormattedText($"{step} м", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 10.5, Brushes.White, dpi),
            new Point(18 + len, y - 8));
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static Pen FrozenPen(Color c, double w)
    {
        var p = new Pen(Frozen(c), w);
        p.Freeze();
        return p;
    }
}
