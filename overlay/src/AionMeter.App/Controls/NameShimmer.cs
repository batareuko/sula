using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AionMeter.Core.Events;

namespace AionMeter.App.Controls;

/// <summary>
/// 1 HP: names that shimmer — a gradient of the class colour and a lighter tint sweeping slowly across the name,
/// and a gold → pink → violet one for your own. One brush per class, shared by every row of that class, so ten
/// rows cost one animation each. Off (Settings → Overlay → animated names): plain white names.
/// </summary>
public static class NameShimmer
{
    public static bool Enabled { get; set; } = true;

    private static readonly Dictionary<GameClass, Brush> ByClass = new();
    private static Brush? _self;

    public static Brush For(GameClass cls, bool self)
    {
        if (!Enabled) return Brushes.White;
        if (self) return _self ??= Animated([Color.FromRgb(0xFF, 0xE0, 0x7A), Color.FromRgb(0xFF, 0x8A, 0xD0), Color.FromRgb(0xA9, 0x8B, 0xFF)], 3.2);
        if (ByClass.TryGetValue(cls, out var b)) return b;
        var c = ClassVisuals.ColorOf(cls);
        b = Animated([Mix(c, Colors.White, 0.55), Colors.White, Mix(c, Colors.White, 0.25)], 4.5);
        ByClass[cls] = b;
        return b;
    }

    /// <summary>A repeating horizontal gradient whose offset slides once per <paramref name="seconds"/>.</summary>
    private static Brush Animated(Color[] colors, double seconds)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            SpreadMethod = GradientSpreadMethod.Repeat,
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
        };
        for (var i = 0; i < colors.Length; i++) brush.GradientStops.Add(new GradientStop(colors[i], (double)i / colors.Length));
        brush.GradientStops.Add(new GradientStop(colors[0], 1));
        var move = new TranslateTransform();
        brush.RelativeTransform = move;
        var anim = new DoubleAnimation(0, 1, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(anim, 30); // a soft shimmer; no need to redraw the overlay at 60 fps for it
        move.BeginAnimation(TranslateTransform.XProperty, anim);
        return brush;
    }

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
