using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AionMeter.Core.OneHp;

namespace AionMeter.App.Controls;

/// <summary>
/// 1 HP: the pet radar's picture — soul monsters as dots around you (the monster you hit last), range rings in metres,
/// the pets missing from your collection bright and named, the ones you have small and grey. Game units are
/// centimetres; the map's X axis points up unless <see cref="Rotation"/> turns it.
/// </summary>
public sealed class RadarView : FrameworkElement
{
    private PetRadar.Picture? _picture;
    private IReadOnlySet<int> _have = new HashSet<int>();

    /// <summary>Radius shown, metres.</summary>
    public double RangeMeters { get; set; } = 100;

    /// <summary>Quarter turns clockwise (0–3), to line the radar up with the game's map.</summary>
    public int Rotation { get; set; }

    public void SetData(PetRadar.Picture picture, IReadOnlySet<int> have)
    {
        _picture = picture;
        _have = have;
        InvalidateVisual();
    }

    private static readonly Typeface Face = new("Segoe UI");
    private static readonly Brush Ring = Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    private static readonly Brush Label = Frozen(Color.FromArgb(0xB0, 0xC9, 0xD2, 0xE0));
    private static readonly Brush Owned = Frozen(Color.FromArgb(0xA0, 0x8A, 0x94, 0xA6));
    private static readonly Brush Me = Frozen(Color.FromRgb(0xFF, 0xD3, 0x4D));

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        var c = new Point(w / 2, h / 2);
        var radius = Math.Min(w, h) / 2 - 6;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // rings at a quarter, half and the full range
        foreach (var part in new[] { 0.25, 0.5, 1.0 })
        {
            var r = radius * part;
            dc.DrawEllipse(null, new Pen(Ring, 1), c, r, r);
            var text = new FormattedText($"{RangeMeters * part:0} м", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, 9, Label, dpi);
            dc.DrawText(text, new Point(c.X + 3, c.Y - r + 1));
        }
        dc.DrawLine(new Pen(Ring, 1), new Point(c.X, c.Y - radius), new Point(c.X, c.Y + radius));
        dc.DrawLine(new Pen(Ring, 1), new Point(c.X - radius, c.Y), new Point(c.X + radius, c.Y));

        if (_picture is not { Dots.Count: > 0 } pic)
        {
            dc.DrawEllipse(Me, null, c, 3.5, 3.5);
            return;
        }
        // centre: you (the monster you hit last), else the middle of the souls in view
        var (cx, cy) = pic.Center ?? (pic.Dots.Average(d => d.X), pic.Dots.Average(d => d.Y));
        var scale = radius / (RangeMeters * 100); // centimetres → pixels
        var labelled = 0;
        foreach (var d in pic.Dots.OrderBy(d => _have.Contains(d.Soul.PetId)).ThenBy(d => Dist(d, cx, cy)))
        {
            var (dx, dy) = Turn(d.X - cx, d.Y - cy);
            var p = new Point(c.X + dy * scale, c.Y - dx * scale);
            var far = (p - c).Length > radius;
            if (far) p = c + (p - c) / (p - c).Length * radius; // beyond the range: on the edge
            var owned = _have.Contains(d.Soul.PetId);
            var fill = owned ? Owned : FamilyBrush(d.Soul.Family);
            if (owned) dc.DrawEllipse(fill, null, p, 2.5, 2.5);
            else
            {
                dc.DrawEllipse(fill, new Pen(Brushes.White, far ? 0.6 : 1.2), p, far ? 3 : 4.5, far ? 3 : 4.5);
                if (!far && labelled++ < 6)
                {
                    var text = new FormattedText($"{d.Soul.Pet} · {Dist(d, cx, cy) / 100:0} м", CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, Face, 10, Brushes.White, dpi);
                    // to the right of the dot, or to its left when it would run off the radar
                    var x = p.X + 7 + text.Width <= w - 2 ? p.X + 7 : p.X - 7 - text.Width;
                    dc.DrawText(text, new Point(Math.Max(2, x), p.Y - 7));
                }
            }
        }
        dc.DrawEllipse(Me, new Pen(Brushes.Black, 1), c, 4, 4);
    }

    private (float, float) Turn(float x, float y) => (Rotation & 3) switch
    {
        1 => (-y, x),
        2 => (-x, -y),
        3 => (y, -x),
        _ => (x, y),
    };

    private static double Dist(PetRadar.Dot d, float cx, float cy) => Math.Sqrt((d.X - cx) * (d.X - cx) + (d.Y - cy) * (d.Y - cy));

    private static Brush FamilyBrush(string family) => Frozen(family switch
    {
        "Fera" => Color.FromRgb(0xFF, 0x7A, 0x5C),
        "Natura" => Color.FromRgb(0x6C, 0xD8, 0x7A),
        "Varian" => Color.FromRgb(0x5C, 0xB4, 0xFF),
        "Cogni" => Color.FromRgb(0xC0, 0x8C, 0xFF),
        _ => Color.FromRgb(0xFF, 0xD2, 0x6A),
    });

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
