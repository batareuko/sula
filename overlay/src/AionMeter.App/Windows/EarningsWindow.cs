using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AionMeter.App.Controls;
using AionMeter.App.Services;
using AionMeter.Core;
using AionMeter.Core.Events;

namespace AionMeter.App.Windows;

/// <summary>
/// 1 HP: what you earn (tray 💰, Ctrl+Shift+E) — a small window over the game: per currency the game reports (Kinah, Abyss
/// Points …) the gain this session, in the last hour, today (from the 07:00 UTC reset) and per hour, and what you spent
/// today. Drag moves it.
/// </summary>
public sealed class EarningsWindow : Window
{
    private readonly MeterService _meter;
    private readonly StackPanel _rows = new() { Margin = new Thickness(12, 8, 12, 10) };
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };

    private static readonly Brush Muted = Frozen(Color.FromRgb(0x9A, 0xA4, 0xB8));
    private static readonly Brush Gain = Frozen(Color.FromRgb(0x5E, 0xE0, 0x8C));
    private static readonly Brush Gold = Frozen(Color.FromRgb(0xFF, 0xD3, 0x4D));

    public EarningsWindow(MeterService meter)
    {
        _meter = meter;
        Title = "1 HP Earnings";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        Left = 40;
        Top = 420;
        FontFamily = new FontFamily("Segoe UI");
        WindowMemory.Track(this, "earnings");
        Content = new Border
        {
            CornerRadius = new CornerRadius(12), Background = Frozen(Color.FromArgb(0xCC, 0x0E, 0x13, 0x20)),
            BorderBrush = Frozen(Color.FromArgb(0x50, 0x6E, 0x8B, 0xFF)), BorderThickness = new Thickness(1), Child = _rows,
            ToolTip = UiText.Current.EarningsTip,
        };
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        SourceInitialized += (_, _) =>
            NativeMethods.SetExStyle(new WindowInteropHelper(this).Handle, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        _timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); _timer.Start(); } else _timer.Stop(); };
    }

    public void ApplyScale(int percent)
    {
        var s = percent / 100.0;
        if (Content is FrameworkElement root) root.LayoutTransform = s == 1 ? Transform.Identity : new ScaleTransform(s, s);
        Width = 380 * s;
    }

    public void Refresh()
    {
        var t = UiText.Current;
        var me = _meter.Tracker.SelfName ?? _meter.Settings.LastSelfName ?? "";
        var sums = me.Length == 0 ? [] : _meter.Earnings.Sums(me, DateTimeOffset.UtcNow);
        _rows.Children.Clear();
        _rows.Children.Add(new TextBlock { Text = t.EarningsTitle + (me.Length > 0 ? " · " + me : ""), Foreground = Muted, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 6) });
        if (sums.Count == 0)
        {
            _rows.Children.Add(new TextBlock { Text = t.EarningsEmpty, Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
            return;
        }
        var head = Line(t.EarningsCurrency, t.EarningsSession, t.EarningsHour, t.EarningsToday, Muted, 10.5);
        _rows.Children.Add(head);
        foreach (var s in sums)
        {
            var name = s.Id switch
            {
                ResourceEvent.Kinah => t.EarningsKinah,
                ResourceEvent.AbyssPoints => t.EarningsAbyss,
                _ => t.ResourceName(s.Id) is { Length: > 0 } n && !n.StartsWith('#') ? n : "#" + s.Id,
            };
            var row = Line(name, Plus(s.Session), Plus(s.Hour), Plus(s.Today), Brushes.White, 13);
            _rows.Children.Add(row);
            var detail = $"{t.EarningsBalance} {Format.Grouped(s.Balance)}" + (s.PerHour > 0 ? $" · {Format.Compact(s.PerHour)}/{t.HoursShort}" : "") +
                         (s.SpentToday > 0 ? $" · {t.EarningsSpent} {Format.Compact(s.SpentToday)}" : "");
            _rows.Children.Add(new TextBlock { Text = detail, Foreground = Muted, FontSize = 11, Margin = new Thickness(2, 0, 0, 6) });
        }
    }

    private static string Plus(long v) => v > 0 ? "+" + Format.Compact(v) : "—";

    private static Grid Line(string name, string session, string hour, string today, Brush color, double size)
    {
        var g = new Grid { Margin = new Thickness(2, 1, 2, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        foreach (var _ in new[] { 0, 1, 2 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        var cells = new[] { name, session, hour, today };
        for (var i = 0; i < 4; i++)
        {
            var tb = new TextBlock
            {
                Text = cells[i], FontSize = size, FontWeight = i == 0 || size < 12 ? FontWeights.SemiBold : FontWeights.Bold,
                Foreground = i > 0 && cells[i].StartsWith('+') ? (i == 3 ? Gold : Gain) : color,
                HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(tb, i);
            g.Children.Add(tb);
        }
        return g;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
