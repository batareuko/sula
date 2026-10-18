using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace AionMeter.App.Controls;

/// <summary>
/// 1 HP: a buff on the card as a round badge — a drawn sign of what the buff does (attack, defence, speed, healing,
/// food and scrolls, anything else), a ring that runs down with the time left and a stack count. The game's own buff
/// icons are not in the data the meter has, so the kind is read from the buff's name.
/// </summary>
public sealed class BuffBadge : FrameworkElement
{
    public enum Kind { Other, Attack, Defense, Speed, Heal, Consumable }

    private const double Box = 18; // glyphs are drawn on an 18×18 grid
    private Kind _kind;
    private double _left = 1;
    private int _count;
    private ImageSource? _icon;

    public BuffBadge()
    {
        Width = Height = 26;
        Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.7 };
    }

    /// <summary>What the buff does, the share of its time still left (1 for one without an end) and its stacks.</summary>
    /// <param name="icon">The icon of the skill that puts this buff on, when the meter has learnt it; else the drawn sign.</param>
    public void Set(Kind kind, double left, int count, ImageSource? icon = null)
    {
        if (_kind == kind && Math.Abs(_left - Math.Clamp(left, 0, 1)) < 0.002 && _count == count && ReferenceEquals(_icon, icon)) return;
        _icon = icon;
        _kind = kind;
        _left = Math.Clamp(left, 0, 1);
        _count = count;
        InvalidateVisual();
    }

    /// <summary>The kind of a buff by the words in its (English) name.</summary>
    public static Kind KindOf(string name)
    {
        var n = name.ToLowerInvariant();
        bool Any(params string[] words) => words.Any(n.Contains);
        if (Any("candy", "cookie", "food", "scroll", "potion", "elixir", "dish", "meal", "drink", "serum")) return Kind.Consumable;
        if (Any("speed", "swift", "haste", "movement", "sprint", "flight", "wing", "dash", "agility")) return Kind.Speed;
        if (Any("heal", "recover", "regen", "restor", "life", "vitality", "hp ", "prayer", "blessing", "mend")) return Kind.Heal;
        if (Any("defense", "defence", "block", "shield", "guard", "protect", "barrier", "resist", "armor", "armour", "parry", "evasion", "dodge"))
            return Kind.Defense;
        if (Any("attack", "damage", "power", "crit", "fury", "rage", "strength", "might", "berserk", "sharp", "accuracy", "boost", "focus"))
            return Kind.Attack;
        return Kind.Other;
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    private static readonly Brush Disc = Freeze(new RadialGradientBrush(Color.FromRgb(0x2A, 0x33, 0x4A), Color.FromRgb(0x10, 0x15, 0x22)));
    private static readonly Pen Track = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), 2));
    private static readonly Typeface Face = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private static Color ColorOf(Kind k) => k switch
    {
        Kind.Attack => Color.FromRgb(0xFF, 0x6A, 0x5A),
        Kind.Defense => Color.FromRgb(0x6A, 0xB4, 0xFF),
        Kind.Speed => Color.FromRgb(0x5E, 0xE0, 0xC0),
        Kind.Heal => Color.FromRgb(0x6E, 0xE0, 0x7A),
        Kind.Consumable => Color.FromRgb(0xFF, 0xB0, 0x48),
        _ => Color.FromRgb(0xF2, 0xD2, 0x7A),
    };

    private static readonly Geometry Sword = Freeze(Geometry.Parse(
        "M14.8,1.6 L16.4,3.2 L8.2,11.4 L6.6,9.8 Z M4.6,9.4 L8.6,13.4 L7.6,14.4 L6.4,13.2 L3.6,16 L2,14.4 L4.8,11.6 L3.6,10.4 Z"));
    private static readonly Geometry Shield = Freeze(Geometry.Parse("M9,1.5 L15.5,4 L15.5,8.6 C15.5,12.6 12.6,15.3 9,16.6 C5.4,15.3 2.5,12.6 2.5,8.6 L2.5,4 Z"));
    private static readonly Geometry Wing = Freeze(Geometry.Parse("M2,13 C5,6 10,3 16.5,2.5 C14.5,4.5 13.5,6 13,7.5 L15,7.5 C13,9.5 11.5,10.5 9.5,11 L11.5,11.5 C8.5,13.5 5.5,14 2,13 Z"));
    private static readonly Geometry Cross = Freeze(Geometry.Parse("M7,2.5 L11,2.5 L11,7 L15.5,7 L15.5,11 L11,11 L11,15.5 L7,15.5 L7,11 L2.5,11 L2.5,7 L7,7 Z"));
    private static readonly Geometry Flask = Freeze(Geometry.Parse("M7,1.8 L11,1.8 L11,3.2 L10.4,3.2 L10.4,6.8 L15,14.2 C15.6,15.4 14.9,16.4 13.6,16.4 L4.4,16.4 C3.1,16.4 2.4,15.4 3,14.2 L7.6,6.8 L7.6,3.2 L7,3.2 Z"));
    private static readonly Geometry Star = Freeze(Geometry.Parse("M9,1.5 L11.2,6.6 L16.6,7 L12.5,10.6 L13.8,16 L9,13.1 L4.2,16 L5.5,10.6 L1.4,7 L6.8,6.6 Z"));

    private static Geometry GlyphOf(Kind k) => k switch
    {
        Kind.Attack => Sword,
        Kind.Defense => Shield,
        Kind.Speed => Wing,
        Kind.Heal => Cross,
        Kind.Consumable => Flask,
        _ => Star,
    };

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var c = new Point(size / 2, size / 2);
        var r = size / 2 - 1.5;
        var color = ColorOf(_kind);
        dc.DrawEllipse(Disc, null, c, r, r);
        dc.DrawEllipse(null, Track, c, r, r);

        // the time left: an arc from the top, clockwise
        var ring = new Pen(new SolidColorBrush(color), 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (_left >= 0.999) dc.DrawEllipse(null, ring, c, r, r);
        else if (_left > 0.005)
        {
            var angle = _left * 2 * Math.PI;
            var start = new Point(c.X, c.Y - r);
            var end = new Point(c.X + r * Math.Sin(angle), c.Y - r * Math.Cos(angle));
            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                g.BeginFigure(start, false, false);
                g.ArcTo(end, new Size(r, r), 0, angle > Math.PI, SweepDirection.Clockwise, true, false);
            }
            dc.DrawGeometry(null, ring, arc);
        }

        if (_icon is not null)
        {
            // the skill's own icon inside the ring
            var ri = r - 2;
            dc.PushClip(new EllipseGeometry(c, ri, ri));
            dc.DrawImage(_icon, new Rect(c.X - ri, c.Y - ri, ri * 2, ri * 2));
            dc.Pop();
        }
        else
        {
            // the sign, at about 60 % of the badge
            var s = size * 0.58 / Box;
            dc.PushTransform(new TranslateTransform(c.X - Box * s / 2, c.Y - Box * s / 2));
            dc.PushTransform(new ScaleTransform(s, s));
            var fill = new LinearGradientBrush(Mix(color, Colors.White, 0.35), color, new Point(0.2, 0), new Point(0.8, 1));
            dc.DrawGeometry(fill, null, GlyphOf(_kind));
            dc.Pop();
            dc.Pop();
        }

        if (_count > 1)
        {
            var text = new FormattedText("×" + _count, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Face, size * 0.34, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var at = new Point(size - text.Width + 1, size - text.Height + 2);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0xD0, 0x0B, 0x0F, 0x18)), null,
                new Rect(at.X - 1.5, at.Y + 1, text.Width + 3, text.Height - 2), 3, 3);
            dc.DrawText(text, at);
        }
    }

    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
}
