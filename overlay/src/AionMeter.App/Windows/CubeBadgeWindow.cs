using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AionMeter.App.Services;

namespace AionMeter.App.Windows;

/// <summary>
/// 1 HP: the Hidden Cube badge — "Cube detected" above the character while a hidden cube is near (Settings → 1 HP).
/// Shown only while the cube radar is on and a cube is detected; clicks pass through it to the game. The badge's text
/// is a placeholder until the cube's position packet is confirmed, then a direction arrow replaces it.
/// </summary>
public sealed class CubeBadgeWindow : Window
{
    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x39, 0xFF, 0x14));

    private readonly TextBlock _text = new()
    {
        FontSize = 20,
        FontWeight = FontWeights.Bold,
        Foreground = Green,
        HorizontalAlignment = HorizontalAlignment.Center,
        // A black halo around the glyphs reads as an outline on any game backdrop.
        Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0, Opacity = 1 },
    };

    public CubeBadgeWindow()
    {
        Title = "1 HP Cube";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 260;
        SizeToContent = SizeToContent.Height;
        FontFamily = new FontFamily("Segoe UI");

        Content = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0E, 0x13, 0x20)),
            BorderBrush = Green,
            BorderThickness = new Thickness(2),
            Padding = new Thickness(16, 8, 16, 10),
            // A soft green glow around the badge.
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Green.Color, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.7 },
            Child = _text,
        };

        SourceInitialized += (_, _) =>
            NativeMethods.SetExStyle(new WindowInteropHelper(this).Handle,
                NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT);
    }

    /// <summary>1 HP: interface scale — the badge and its window grow together.</summary>
    public void ApplyScale(int percent)
    {
        var s = percent / 100.0;
        if (Content is FrameworkElement root) root.LayoutTransform = s == 1 ? Transform.Identity : new ScaleTransform(s, s);
        Width = 260 * s;
    }

    /// <summary>Shows the badge with the number of cubes near, or hides when there are none (or the radar is off).</summary>
    public void Update(bool detected, int count)
    {
        if (!detected)
        {
            if (IsVisible) Hide();
            return;
        }
        _text.Text = count > 1 ? $"{UiText.Current.CubeDetected} ×{count}" : UiText.Current.CubeDetected;
        if (!IsVisible)
        {
            var area = SystemParameters.WorkArea;
            Left = area.Left + (area.Width - Width) / 2;
            Top = area.Top + area.Height * 0.40; // centred, just above the character
            Show();
        }
    }
}
