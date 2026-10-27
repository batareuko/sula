using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace AionMeter.App.Controls;

/// <summary>
/// 1 HP: a small drawn sign for a timed event on the card's event strip — Shugo Festival (gold coin with a face),
/// Spacetime Rift (violet portal), Invasion (red crossed blades), siege (tower), weekly reset (calendar). Vector,
/// so nothing is downloaded.
/// </summary>
public sealed class EventIcon : FrameworkElement
{
    public enum Kind { Shugo, Rift, Invasion, Siege, Weekly }

    private const double Box = 18;
    private Kind _kind;

    private ImageSource? _image;

    /// <summary>1 HP: the game's own icon (from the game-data CDN); the drawn sign stands in until it is there.</summary>
    public ImageSource? Image
    {
        get => _image;
        set
        {
            if (ReferenceEquals(_image, value)) return;
            _image = value;
            InvalidateVisual();
        }
    }

    /// <summary>1 HP: how much to enlarge <see cref="Image"/> — the game's map markers have wide transparent margins.</summary>
    public double Zoom { get; set; } = 1;

    public EventIcon(Kind kind)
    {
        _kind = kind;
        Width = Height = 18;
        Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0.5, Opacity = 0.8 };
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
    private static Brush Linear(Color a, Color b) => Freeze(new LinearGradientBrush(a, b, new Point(0.2, 0), new Point(0.8, 1)));

    private static readonly Brush Gold = Linear(Color.FromRgb(0xFF, 0xE6, 0x8A), Color.FromRgb(0xC8, 0x86, 0x1E));
    private static readonly Pen GoldEdge = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x7A, 0x4E, 0x0E)), 0.8));
    private static readonly Brush Dark = Freeze(new SolidColorBrush(Color.FromRgb(0x4A, 0x2C, 0x08)));
    private static readonly Brush Violet = Freeze(new RadialGradientBrush(Color.FromRgb(0x1A, 0x08, 0x30), Color.FromRgb(0xB0, 0x7C, 0xFF)));
    private static readonly Pen VioletRing = Freeze(new Pen(Linear(Color.FromRgb(0xE2, 0xC8, 0xFF), Color.FromRgb(0x7A, 0x3C, 0xD8)), 1.4));
    private static readonly Brush Red = Linear(Color.FromRgb(0xFF, 0x9A, 0x8A), Color.FromRgb(0xC8, 0x1E, 0x2E));
    private static readonly Pen RedEdge = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x5A, 0x0A, 0x10)), 0.7));
    private static readonly Brush Stone = Linear(Color.FromRgb(0xD8, 0xDE, 0xEA), Color.FromRgb(0x7A, 0x86, 0x9C));
    private static readonly Pen StoneEdge = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x40)), 0.7));
    private static readonly Brush Teal = Linear(Color.FromRgb(0xBF, 0xF7, 0xEE), Color.FromRgb(0x2D, 0xB4, 0xA0));
    private static readonly Pen TealEdge = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x0E, 0x4A, 0x42)), 0.7));
    private static readonly Pen Spiral = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0xD0, 0xFF, 0xFF, 0xFF)), 0.9));

    private static readonly Geometry Blades = Freeze(Geometry.Parse(
        "M2,2 L4.2,2 L13.6,11.4 L12.2,12.8 Z M16,2 L13.8,2 L4.4,11.4 L5.8,12.8 Z " +
        "M11,12.6 L14.6,16.2 L16.2,14.6 L12.6,11 Z M7,12.6 L3.4,16.2 L1.8,14.6 L5.4,11 Z"));
    private static readonly Geometry Tower = Freeze(Geometry.Parse(
        "M3,16.5 L3,5 L5,5 L5,3 L7,3 L7,5 L8,5 L8,3 L10,3 L10,5 L11,5 L11,3 L13,3 L13,5 L15,5 L15,16.5 Z"));
    private static readonly Geometry Gate = Freeze(Geometry.Parse("M7,16.5 L7,12 A2,2 0 0 1 11,12 L11,16.5 Z"));
    private static readonly Geometry Calendar = Freeze(Geometry.Parse("M2.5,4 L15.5,4 L15.5,16 L2.5,16 Z"));
    private static readonly Geometry CalendarTop = Freeze(Geometry.Parse("M2.5,4 L15.5,4 L15.5,7.2 L2.5,7.2 Z"));
    private static readonly Geometry Swirl = Freeze(Geometry.Parse("M9,9 m-1,0 a1,1 0 1 1 2,0 a2.4,2.4 0 1 1 -4.4,0.4 a3.8,3.8 0 1 1 7.4,0.8"));

    protected override void OnRender(DrawingContext dc)
    {
        var s = Math.Min(ActualWidth, ActualHeight) / Box;
        if (s <= 0) return;
        if (_image is not null)
        {
            var w = ActualWidth * Zoom;
            var h = ActualHeight * Zoom;
            dc.DrawImage(_image, new Rect((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h));
            return;
        }
        dc.PushTransform(new ScaleTransform(s, s));
        switch (_kind)
        {
            case Kind.Shugo: // coin with a little Shugo face
                dc.DrawEllipse(Gold, GoldEdge, new Point(9, 9), 7.6, 7.6);
                dc.DrawEllipse(Dark, null, new Point(6.6, 8), 1.1, 1.3);
                dc.DrawEllipse(Dark, null, new Point(11.4, 8), 1.1, 1.3);
                dc.DrawGeometry(null, Freeze(new Pen(Dark, 1)), Geometry.Parse("M6.4,11.4 Q9,13.6 11.6,11.4"));
                dc.DrawGeometry(Gold, GoldEdge, Geometry.Parse("M3.4,4.6 L2,1.4 L5.8,2.8 Z M14.6,4.6 L16,1.4 L12.2,2.8 Z"));
                break;
            case Kind.Rift:
                dc.DrawEllipse(Violet, VioletRing, new Point(9, 9), 7.4, 7.4);
                dc.DrawGeometry(null, Spiral, Swirl);
                break;
            case Kind.Invasion:
                dc.DrawGeometry(Red, RedEdge, Blades);
                break;
            case Kind.Siege:
                dc.DrawGeometry(Stone, StoneEdge, Tower);
                dc.DrawGeometry(Freeze(new SolidColorBrush(Color.FromRgb(0x20, 0x26, 0x34))), null, Gate);
                break;
            case Kind.Weekly:
                dc.DrawGeometry(Teal, TealEdge, Calendar);
                dc.DrawGeometry(Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x6E, 0x62))), null, CalendarTop);
                dc.DrawRectangle(TealEdge.Brush, null, new Rect(5, 2.2, 1.4, 3.2));
                dc.DrawRectangle(TealEdge.Brush, null, new Rect(11.6, 2.2, 1.4, 3.2));
                dc.DrawGeometry(null, Freeze(new Pen(TealEdge.Brush, 1.2)), Geometry.Parse("M6,11.6 L8.2,13.6 L12.2,9.2"));
                break;
        }
        dc.Pop();
    }
}
