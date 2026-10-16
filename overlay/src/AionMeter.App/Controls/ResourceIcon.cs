using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using AionMeter.Core.Events;

namespace AionMeter.App.Controls;

/// <summary>
/// 1 HP: a small drawn icon for a character resource on the card — aether (blue crystal), Shugo keys (gold key),
/// Nightmare (purple moon), Invasion (red portal), Awakening (gold star). Vector, so it needs no downloads.
/// </summary>
public sealed class ResourceIcon : FrameworkElement
{
    private const double Box = 18; // the shapes are drawn on an 18×18 grid and scaled to the control

    public static readonly DependencyProperty ResourceIdProperty = DependencyProperty.Register(
        nameof(ResourceId), typeof(int), typeof(ResourceIcon), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public int ResourceId
    {
        get => (int)GetValue(ResourceIdProperty);
        set => SetValue(ResourceIdProperty, value);
    }

    public ResourceIcon()
    {
        Width = Height = 16;
        Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 0.5, Opacity = 0.8 };
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static Brush Linear(Color a, Color b) => Freeze(new LinearGradientBrush(a, b, new Point(0.2, 0), new Point(0.8, 1)));
    private static Brush Radial(Color a, Color b) => Freeze(new RadialGradientBrush(a, b) { GradientOrigin = new Point(0.35, 0.3) });

    private static readonly Brush AetherFill = Linear(Color.FromRgb(0x9E, 0xF0, 0xFF), Color.FromRgb(0x1E, 0x6F, 0xE0));
    private static readonly Pen AetherEdge = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xD8, 0xFA, 0xFF)), 0.9));
    private static readonly Brush Gold = Linear(Color.FromRgb(0xFF, 0xE6, 0x8A), Color.FromRgb(0xC8, 0x86, 0x1E));
    private static readonly Pen GoldEdge = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x7A, 0x4E, 0x0E)), 0.8));
    private static readonly Brush MoonFill = Linear(Color.FromRgb(0xE2, 0xB8, 0xFF), Color.FromRgb(0x7A, 0x3C, 0xD8));
    private static readonly Brush PortalFill = Radial(Color.FromRgb(0x2A, 0x06, 0x0C), Color.FromRgb(0xFF, 0x4A, 0x4A));
    private static readonly Pen PortalRing = Freeze(new Pen(Linear(Color.FromRgb(0xFF, 0x9A, 0x6A), Color.FromRgb(0xC8, 0x1E, 0x2E)), 1.6));
    private static readonly Brush Highlight = Freeze(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)));
    private static readonly Brush Plain = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0x94, 0xA8)));

    private static readonly Geometry Crystal = Freeze(Geometry.Parse("M9,0.8 L15.6,7 L9,17.2 L2.4,7 Z"));
    private static readonly Geometry CrystalFacet = Freeze(Geometry.Parse("M9,0.8 L6.2,7 L9,17.2 M2.4,7 L15.6,7"));
    private static readonly Geometry KeyBow = Freeze(Geometry.Parse("M5.5,1.5 A4,4 0 1 1 5.49,1.5 Z M5.5,3.7 A1.8,1.8 0 1 0 5.51,3.7 Z"));
    private static readonly Geometry KeyShaft = Freeze(Geometry.Parse("M8.6,7.2 L16.6,15.2 L15.2,16.6 L13.9,15.3 L12.6,16.6 L11.3,15.3 L12.6,14 L7.2,8.6 Z"));
    private static readonly Geometry Moon = Freeze(Geometry.Parse("M11.5,1.2 A8,8 0 1 0 16.8,12.6 A6.4,6.4 0 1 1 11.5,1.2 Z"));
    private static readonly Geometry Spark = Freeze(Geometry.Parse("M14,2 L14.7,3.9 L16.6,4.6 L14.7,5.3 L14,7.2 L13.3,5.3 L11.4,4.6 L13.3,3.9 Z"));
    private static readonly Geometry Star = Freeze(Star8());

    private static Geometry Star8()
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            for (var i = 0; i < 16; i++)
            {
                var r = i % 2 == 0 ? 8.6 : 3.6;
                var a = Math.PI * i / 8 - Math.PI / 2;
                var p = new Point(9 + r * Math.Cos(a), 9 + r * Math.Sin(a));
                if (i == 0) c.BeginFigure(p, true, true);
                else c.LineTo(p, true, true);
            }
        }
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var s = Math.Min(ActualWidth, ActualHeight) / Box;
        if (s <= 0) return;
        dc.PushTransform(new ScaleTransform(s, s));
        switch (ResourceId)
        {
            case ResourceEvent.Aether:
                dc.DrawGeometry(AetherFill, AetherEdge, Crystal);
                dc.DrawGeometry(null, AetherEdge, CrystalFacet);
                break;
            case ResourceEvent.ShugoKeys:
                dc.DrawGeometry(Gold, GoldEdge, KeyShaft);
                dc.DrawGeometry(Gold, GoldEdge, KeyBow);
                break;
            case ResourceEvent.Nightmare:
                dc.DrawGeometry(MoonFill, null, Moon);
                dc.DrawGeometry(Highlight, null, Spark);
                break;
            case ResourceEvent.Invasion:
                dc.DrawEllipse(PortalFill, PortalRing, new Point(9, 9), 7.4, 7.4);
                dc.DrawEllipse(null, Freeze(new Pen(Highlight, 0.7)), new Point(9, 9), 4, 4);
                break;
            case ResourceEvent.Awakening:
                dc.DrawGeometry(Gold, GoldEdge, Star);
                dc.DrawEllipse(Highlight, null, new Point(9, 9), 1.8, 1.8);
                break;
            default:
                dc.DrawEllipse(Plain, null, new Point(9, 9), 6, 6);
                break;
        }
        dc.Pop();
    }
}
