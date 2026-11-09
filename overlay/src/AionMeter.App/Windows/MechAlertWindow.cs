using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AionMeter.App.Services;
using AionMeter.Core.OneHp;

namespace AionMeter.App.Windows;

/// <summary>
/// 1 HP: mechanic warnings over the game (Settings → 1 HP): the most urgent one big — icon, title, what to do and what
/// comes after — and up to two more below it. Shown only while one is due; clicks go through it to the game.
/// </summary>
public sealed class MechAlertWindow : Window
{
    private const double BaseWidth = 780;
    private readonly StackPanel _stack = new();
    private nint _hwnd;

    public MechAlertWindow()
    {
        Title = "1 HP Mechanics";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = BaseWidth;
        SizeToContent = SizeToContent.Height;
        FontFamily = new FontFamily("Segoe UI");
        Content = _stack;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.SetExStyle(_hwnd, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT);
        };
    }

    public void ApplyScale(int percent)
    {
        var s = percent / 100.0;
        _stack.LayoutTransform = s == 1 ? Transform.Identity : new ScaleTransform(s, s);
        Width = BaseWidth * s;
    }

    /// <summary>False for the preview picture: the content is built, the window is never put on the screen.</summary>
    public bool AutoShow { get; init; } = true;

    private string _shown = "";

    /// <summary>Shows the warnings (the first one big), or hides when there are none.</summary>
    public void Update(IReadOnlyList<BossAlerts.Banner> banners)
    {
        if (banners.Count == 0)
        {
            _shown = "";
            if (IsVisible) Hide();
            return;
        }
        // the big one blinks while it is "now"; everything else only changes with the countdown
        var blink = banners[0].Stage == BossAlerts.Stage.Now && Environment.TickCount64 / 400 % 2 == 0;
        var key = string.Join("|", banners.Take(3).Select(b => $"{b.Alert.Id}:{b.Stage}:{(b.LeftSec is { } l ? Math.Ceiling(l) : -1)}")) + blink;
        if (key != _shown)
        {
            _shown = key;
            _stack.Children.Clear();
            _stack.Children.Add(Big(banners[0], blink));
            foreach (var b in banners.Skip(1).Take(2)) _stack.Children.Add(Small(b));
        }
        if (!IsVisible && AutoShow)
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + area.Height * 0.16;
            Show();
        }
    }

    private static string StageText(BossAlerts.Banner b)
    {
        var t = UiText.Current;
        return b.Stage == BossAlerts.Stage.Now ? t.MechNowStage
            : b.LeftSec is { } s ? string.Format(t.MechSoonSec, Math.Ceiling(s))
            : b.AtHp is { } hp ? string.Format(t.MechSoonHp, hp)
            : t.MechSoonStage;
    }

    private static Brush StageBrush(BossAlerts.Banner b) =>
        new SolidColorBrush(b.Stage == BossAlerts.Stage.Now ? Color.FromRgb(0xFF, 0x4D, 0x5E) : Color.FromRgb(0xFF, 0xC1, 0x3D));

    private static TextBlock Line(string text, double size, FontWeight weight, Brush brush) => new()
    {
        Text = text, FontSize = size, FontWeight = weight, Foreground = brush, TextWrapping = TextWrapping.Wrap,
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.9 },
    };

    private static UIElement Big(BossAlerts.Banner b, bool blink)
    {
        var a = b.Alert;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) };
        text.Children.Add(Line(StageText(b), 15, FontWeights.Bold, StageBrush(b)));
        text.Children.Add(Line(a.Title, 36, FontWeights.Black, Brushes.White));
        if (a.Do.Length > 0) text.Children.Add(Line(a.Do, 19, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xFA))));
        if (a.Then is { Length: > 0 } then)
            text.Children.Add(Line(string.Format(UiText.Current.MechThen, then), 15.5, FontWeights.SemiBold, new SolidColorBrush(Color.FromRgb(0x9F, 0xD8, 0xFF))));
        var row = new DockPanel();
        var icon = MechIcon(a.Icon, 84);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        row.Children.Add(text);
        return new Border
        {
            CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 12, 18, 14),
            Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x10, 0x14, 0x22)),
            BorderThickness = new Thickness(3),
            BorderBrush = b.Stage == BossAlerts.Stage.Now ? (blink ? Brushes.White : StageBrush(b)) : StageBrush(b),
            Child = row,
        };
    }

    private static UIElement Small(BossAlerts.Banner b)
    {
        var row = new DockPanel();
        var icon = MechIcon(b.Alert.Icon, 34);
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);
        var stage = Line(StageText(b), 14, FontWeights.Bold, StageBrush(b));
        stage.VerticalAlignment = VerticalAlignment.Center;
        stage.Margin = new Thickness(10, 0, 0, 0);
        DockPanel.SetDock(stage, Dock.Right);
        row.Children.Add(stage);
        var title = Line(b.Alert.Title, 20, FontWeights.Bold, Brushes.White);
        title.VerticalAlignment = VerticalAlignment.Center;
        title.Margin = new Thickness(10, 0, 0, 0);
        row.Children.Add(title);
        return new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 6, 12, 6), Margin = new Thickness(40, 6, 40, 0),
            Background = new SolidColorBrush(Color.FromArgb(0xC8, 0x10, 0x14, 0x22)), Child = row,
        };
    }

    /// <summary>Drawn icons (no picture files): a coloured disc with a white sign — jump, stack, orb, shield and the rest.</summary>
    internal static FrameworkElement MechIcon(string kind, double size)
    {
        var (color, path, fill) = kind switch
        {
            "jump" => ("#3B82F6", "M12,3 L12,15 M6,9 L12,3 L18,9 M4,20 L20,20", false),
            "stack" => ("#16A34A", "M3,3 L9,9 M9,4 L9,9 L4,9 M21,3 L15,9 M15,4 L15,9 L20,9 M3,21 L9,15 M4,15 L9,15 L9,20 M21,21 L15,15 M20,15 L15,15 L15,20", false),
            "spread" => ("#D97706", "M9,9 L3,3 M3,8 L3,3 L8,3 M15,9 L21,3 M16,3 L21,3 L21,8 M9,15 L3,21 M3,16 L3,21 L8,21 M15,15 L21,21 M21,16 L21,21 L16,21", false),
            "orb" => ("#DC2626", "M12,5 A7,7 0 1 1 11.99,5 Z M9,9 A2,2 0 0 1 11,8", false),
            "defense" => ("#0284C7", "M12,2 L20,5 L20,11 C20,16 16,20 12,22 C8,20 4,16 4,11 L4,5 Z M8,12 L11,15 L16,9", false),
            "safe" => ("#0891B2", "M5,3 L19,3 L19,21 L5,21 Z M9,3 L9,21 M15,3 L15,21 M8,12 L11,15 L16,9", false),
            "dodge" => ("#D97706", "M3,12 L19,12 M13,6 L19,12 L13,18 M3,6 L3,18", false),
            "laser" => ("#E11D48", "M3,21 L21,3 M15,3 L21,3 L21,9 M3,15 L7,19", false),
            "enrage" => ("#B91C1C", "M12,2 C16,7 19,10 19,14 A7,7 0 0 1 5,14 C5,11 7,9 9,7 C9,10 10,11 12,12 C12,9 11,5 12,2 Z", true),
            "adds" => ("#9333EA", "M4,8 A3,3 0 1 0 10,8 A3,3 0 1 0 4,8 M14,8 A3,3 0 1 0 20,8 A3,3 0 1 0 14,8 M9,17 A3,3 0 1 0 15,17 A3,3 0 1 0 9,17", false),
            "bomb" => ("#7E22CE", "M3,14 A7,7 0 1 0 17,14 A7,7 0 1 0 3,14 M14,8 L18,4 M17,2 L19,4 M20,5 L22,5", false),
            "look" => ("#CA8A04", "M2,12 C6,5 18,5 22,12 C18,19 6,19 2,12 Z M9,12 A3,3 0 1 0 15,12 A3,3 0 1 0 9,12", false),
            "color" => ("", "M10,12 L3,12 M6,9 L3,12 L6,15 M14,12 L21,12 M18,9 L21,12 L18,15", false),
            "break" => ("#EA580C", "M12,2 L9,10 L14,12 L10,22 M4,5 L8,8 M20,5 L16,8 M4,19 L7,17", false),
            _ => ("#D97706", "M12,3 L22,21 L2,21 Z M12,9 L12,15 M12,17.5 L12,18.5", false),
        };
        Brush disc = kind == "color"
            ? new LinearGradientBrush(
                new GradientStopCollection { new(Color.FromRgb(0xDC, 0x26, 0x26), 0), new(Color.FromRgb(0xDC, 0x26, 0x26), 0.5), new(Color.FromRgb(0x25, 0x63, 0xEB), 0.5), new(Color.FromRgb(0x25, 0x63, 0xEB), 1) },
                new Point(0, 0.5), new Point(1, 0.5))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        var sign = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(path), Stroke = Brushes.White, StrokeThickness = 2.2, Fill = fill ? Brushes.White : null,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            Width = 24, Height = 24, Stretch = Stretch.None,
        };
        var inner = new Viewbox { Child = new Canvas { Width = 24, Height = 24, Children = { sign } }, Margin = new Thickness(size * 0.2) };
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), Background = disc, VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)), BorderThickness = new Thickness(2), Child = inner,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 10, ShadowDepth = 0, Opacity = 0.7, Color = Colors.Black },
        };
    }
}
