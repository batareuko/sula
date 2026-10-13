using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace AionMeter.App.Controls;

/// <summary>
/// 1 HP: a skill on cooldown as the game draws it — the skill icon in a circle, a dark sector that drains clockwise as
/// the skill comes back, a ring round it and the seconds left in the middle (green in the last 5 s).
/// </summary>
public sealed class CooldownBadge : FrameworkElement
{
    private const double Size = 34;
    private static readonly Brush Shade = Freeze(new SolidColorBrush(Color.FromArgb(0xB0, 0x05, 0x08, 0x10)));
    private static readonly Brush Fallback = Freeze(new SolidColorBrush(Color.FromRgb(0x2A, 0x33, 0x48)));
    private static readonly Pen Ring = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x5A)), 2));
    private static readonly Pen RingReady = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x5E, 0xE0, 0x9A)), 2));
    private static readonly Pen Track = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), 2));
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private ImageSource? _icon;
    private double _left = 1;
    private long _leftMs;

    public CooldownBadge()
    {
        Width = Size;
        Height = Size;
        Margin = new Thickness(0, 0, 6, 0);
        Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.7 };
    }

    /// <summary>The icon (null: a plain disc), the share of the cooldown still to run (1 → 0) and the time left.</summary>
    public void Set(ImageSource? icon, double left, long leftMs)
    {
        _icon = icon;
        _left = Math.Clamp(left, 0, 1);
        _leftMs = leftMs;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var c = new Point(Size / 2, Size / 2);
        var r = Size / 2 - 2;
        var disc = new EllipseGeometry(c, r, r);
        dc.PushClip(disc);
        if (_icon is not null) dc.DrawImage(_icon, new Rect(c.X - r, c.Y - r, r * 2, r * 2));
        else dc.DrawGeometry(Fallback, null, disc);
        // the part still cooling down, from 12 o'clock clockwise
        if (_left > 0) dc.DrawGeometry(Shade, null, _left >= 0.999 ? disc : Sector(c, r + 1, _left));
        dc.Pop();

        dc.DrawEllipse(null, Track, c, r, r);
        var ready = _leftMs <= 5_000;
        if (_left > 0) dc.DrawGeometry(null, ready ? RingReady : Ring, Arc(c, r, 1 - _left));

        var seconds = _leftMs >= 60_000 ? $"{_leftMs / 60_000}:{_leftMs / 1000 % 60:00}" : $"{Math.Ceiling(_leftMs / 1000.0)}";
        var text = new FormattedText(seconds, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face,
            seconds.Length > 3 ? 10 : 12, ready ? RingReady.Brush : Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var at = new Point(c.X - text.Width / 2, c.Y - text.Height / 2);
        dc.DrawGeometry(Brushes.Black, new Pen(Brushes.Black, 2.5) { LineJoin = PenLineJoin.Round }, text.BuildGeometry(at));
        dc.DrawText(text, at);
    }

    /// <summary>The pie of <paramref name="share"/> of the circle starting where <see cref="Arc"/> ends (clockwise).</summary>
    private static Geometry Sector(Point c, double r, double share)
    {
        var start = 1 - share;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(c, true, true);
            ctx.LineTo(OnCircle(c, r, start), true, false);
            ctx.ArcTo(OnCircle(c, r, 1), new Size(r, r), 0, share > 0.5, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    /// <summary>The ring from 12 o'clock clockwise over <paramref name="share"/> of the circle — the time already run.</summary>
    private static Geometry Arc(Point c, double r, double share)
    {
        var g = new StreamGeometry();
        if (share <= 0) return g;
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(OnCircle(c, r, 0), false, false);
            ctx.ArcTo(OnCircle(c, r, Math.Min(share, 0.9999)), new Size(r, r), 0, share > 0.5, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    private static Point OnCircle(Point c, double r, double share)
    {
        var a = share * 2 * Math.PI - Math.PI / 2;
        return new Point(c.X + r * Math.Cos(a), c.Y + r * Math.Sin(a));
    }

    private static T Freeze<T>(T f) where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
