using System.IO;
using System.Windows;

namespace AionMeter.App.Services;

/// <summary>
/// 1 HP: the app's other windows (history, breakdown, settings, boss timers, update) open where they were last closed,
/// at the last size, instead of the middle of the screen. One place per kind of window; a place that is no longer on
/// any monitor is ignored.
/// </summary>
public static class WindowMemory
{
    private static AppSettings? _settings;

    /// <summary>Set once at start; without it (render tool) windows keep their default place.</summary>
    public static void Use(AppSettings settings) => _settings = settings;

    public static void Track(Window window, string kind)
    {
        if (_settings is not { } settings) return;
        if (settings.WindowPlaces.TryGetValue(kind, out var b) && OnScreen(b))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = b.Left;
            window.Top = b.Top;
            window.Width = Math.Max(window.MinWidth, b.Width);
            window.Height = Math.Max(window.MinHeight, b.Height);
        }
        window.Closing += (_, _) =>
        {
            var r = window.WindowState == WindowState.Normal
                ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
                : window.RestoreBounds;
            if (r.IsEmpty || r.Width < 1 || double.IsNaN(r.Left)) return;
            settings.WindowPlaces[kind] = new WindowBounds(Math.Round(r.Left), Math.Round(r.Top), Math.Round(r.Width), Math.Round(r.Height));
            try
            {
                settings.Save();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        };
    }

    /// <summary>The title area (top 40 DIP, 60 DIP wide) must be on the virtual screen to be able to drag it back.</summary>
    public static bool OnScreen(WindowBounds b)
    {
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;
        return b.Width > 0 && b.Height > 0 &&
               b.Left + 60 <= right && b.Left + b.Width - 60 >= left &&
               b.Top >= top - 10 && b.Top + 40 <= bottom;
    }
}
