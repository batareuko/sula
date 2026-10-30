using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AionMeter.App.Services;
using AionMeter.Core;
using AionMeter.Core.Combat;

namespace AionMeter.App.Windows;

/// <summary>
/// 1 HP: the boss's HP as a wide bar at the top of the screen (Settings → Overlay): name, HP, per cent, the kill
/// forecast and the enrage. Shown only in a live boss fight; clicks go through it to the game.
/// </summary>
public sealed class BossBarWindow : Window
{
    private readonly TextBlock _name = Text(17, FontWeights.Bold);
    private readonly TextBlock _hp = Text(14, FontWeights.SemiBold);
    private readonly TextBlock _percent = Text(17, FontWeights.Bold);
    private readonly TextBlock _forecast = Text(13, FontWeights.SemiBold);
    private readonly ScaleTransform _fill = new(1, 1);
    private nint _hwnd;

    private static TextBlock Text(double size, FontWeight weight) => new()
    {
        FontSize = size, FontWeight = weight, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center,
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.8 },
    };

    /// <summary>1 HP: interface scale — the bar and its window grow together.</summary>
    public void ApplyScale(int percent)
    {
        var s = percent / 100.0;
        if (Content is FrameworkElement root) root.LayoutTransform = s == 1 ? Transform.Identity : new ScaleTransform(s, s);
        Width = 620 * s;
    }

    public BossBarWindow()
    {
        Title = "1 HP Boss";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 620;
        SizeToContent = SizeToContent.Height;
        FontFamily = new FontFamily("Segoe UI");

        var bar = new Grid { Height = 26, Margin = new Thickness(0, 6, 0, 0) };
        bar.Children.Add(new Border { CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x2A, 0x14, 0x20)) });
        bar.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(7), RenderTransformOrigin = new Point(0, 0.5), RenderTransform = _fill,
            Background = new LinearGradientBrush(Color.FromRgb(0xE1, 0x1D, 0x48), Color.FromRgb(0xFB, 0x71, 0x85), 0),
        });
        var inBar = new Grid { Margin = new Thickness(12, 0, 12, 0) };
        inBar.Children.Add(_hp);
        _forecast.HorizontalAlignment = HorizontalAlignment.Center;
        inBar.Children.Add(_forecast);
        _percent.HorizontalAlignment = HorizontalAlignment.Right;
        inBar.Children.Add(_percent);
        bar.Children.Add(inBar);

        _name.HorizontalAlignment = HorizontalAlignment.Center;
        var stack = new StackPanel { Margin = new Thickness(10, 6, 10, 8) };
        stack.Children.Add(_name);
        stack.Children.Add(bar);
        Content = new Border
        {
            CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromArgb(0x99, 0x0E, 0x13, 0x20)), Child = stack,
        };

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetExStyle(_hwnd, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT);
        };
    }

    /// <summary>Shows the live boss fight, or hides when there is none (or the bar is off).</summary>
    public void Update(EncounterSnapshot? snap, bool enabled)
    {
        if (!enabled || snap is not { IsActive: true, Boss: { MaxHp: > 0, Hp: > 0 } boss })
        {
            if (IsVisible) Hide();
            return;
        }
        _name.Text = boss.Name;
        _hp.Text = Format.Grouped(boss.Hp) + " HP";
        _percent.Text = boss.MaxHpKnown ? Format.Percent(boss.HpFraction) : "?";
        _fill.ScaleX = boss.HpFraction;
        var f = boss.Forecast;
        var t = UiText.Current;
        var parts = new List<string>();
        if (f?.KillSeconds is { } k) parts.Add("⏱ " + Mmss(k));
        if (f?.EnrageLeftSeconds is { } e) parts.Add(string.Format(t.ForecastEnrage, Mmss(e)));
        if (f is { Late: true, MissingDps: > 0 }) parts.Add(string.Format(t.ForecastMissing, Format.Compact(f.MissingDps)));
        _forecast.Text = string.Join(" · ", parts);
        _forecast.Foreground = f?.Late == true ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD0, 0xD0)) : Brushes.White;
        if (!IsVisible)
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + area.Height * 0.06;
            Show();
        }
    }

    private static string Mmss(double seconds) =>
        seconds > 3599 ? ">59:59" : TimeSpan.FromSeconds(Math.Ceiling(Math.Max(0, seconds))).ToString(@"m\:ss");
}
