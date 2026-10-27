using System.Windows;
using System.Windows.Media;

namespace AionMeter.App.Controls;

/// <summary>
/// 1 HP: a skill's damage over the fight as thin bars (skill analysis): one bar per second, scaled to the skill's own
/// best second, so a row shows when the skill was used rather than how much it did.
/// </summary>
public sealed class SparkBars : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(nameof(Values), typeof(IReadOnlyList<long>),
        typeof(SparkBars), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush),
        typeof(SparkBars), new FrameworkPropertyMetadata(Brushes.Gold, FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<long>? Values
    {
        get => (IReadOnlyList<long>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    private static readonly Brush Track = Frozen(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF));

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRectangle(Track, null, new Rect(0, h - 1, w, 1));
        if (Values is not { Count: > 0 } values) return;
        var max = values.Max();
        if (max <= 0) return;
        var step = w / values.Count;
        var bar = Math.Max(1, step - (step > 3 ? 1 : 0));
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] <= 0) continue;
            var bh = Math.Max(2, h * values[i] / max);
            dc.DrawRectangle(Fill, null, new Rect(i * step, h - bh, bar, bh));
        }
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
