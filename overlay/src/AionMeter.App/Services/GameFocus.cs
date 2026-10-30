using System.Windows;
using System.Windows.Threading;
using AionMeter.Core.Capture;

namespace AionMeter.App.Services;

/// <summary>
/// 1 HP: the windows over the game step aside while another program is in front of it (a browser, Discord, the
/// desktop) and come back when the game is in front again. Only the foreground window's process is checked — nothing is
/// read from the game; the game's process ids come from the process list, as for the start-with-game.
/// </summary>
public sealed class GameFocus
{
    private readonly AppSettings _settings;
    private readonly Func<IEnumerable<Window?>> _windows;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private HashSet<uint> _gamePids = new();
    private long _pidsAt = long.MinValue;
    private bool _away;

    /// <summary>Windows the user always reaches the meter through: the taskbar, the tray overflow, Alt+Tab, Start.</summary>
    private static readonly HashSet<string> Shell = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
        "MultitaskingViewFrame", "XamlExplorerHostIslandWindow", "Windows.UI.Core.CoreWindow", "ForegroundStaging",
    };

    public GameFocus(AppSettings settings, Func<IEnumerable<Window?>> windows)
    {
        _settings = settings;
        _windows = windows;
        _timer.Tick += (_, _) => Check();
    }

    public void Start() => _timer.Start();

    /// <summary>True while the game runs and another program's window is in front of it.</summary>
    public bool Away => _away;

    private void Check()
    {
        var away = _settings.HideBehindOtherApps && !_settings.NoGameScan && GameBehind();
        if (away == _away) return;
        _away = away;
        Apply();
    }

    /// <summary>Puts the windows back to the state <see cref="Away"/> asks for (also after one of them opened).</summary>
    public void Apply()
    {
        foreach (var w in _windows())
        {
            if (w is null) continue;
            w.Opacity = _away ? 0 : 1;
            w.IsHitTestVisible = !_away;
        }
    }

    private bool GameBehind()
    {
        var now = Environment.TickCount64;
        if (now - _pidsAt > 5_000)
        {
            _pidsAt = now;
            _gamePids = GameProcessLocator.FindGameProcessIds().Select(id => (uint)id).ToHashSet();
        }
        if (_gamePids.Count == 0) return false; // no game: nothing to step aside for
        var front = NativeMethods.GetForegroundWindow();
        if (front == 0) return false;
        NativeMethods.GetWindowThreadProcessId(front, out var pid);
        if (pid == 0 || pid == _ownPid || _gamePids.Contains(pid)) return false;
        return !Shell.Contains(NativeMethods.ClassName(front));
    }
}

/// <summary>1 HP: the game's window — to blink its taskbar button and to bring it to the front (never clicks or types).</summary>
public static class GameWindow
{
    private static nint Find()
    {
        foreach (var id in GameProcessLocator.FindGameProcessIds())
        {
            try
            {
                using var p = System.Diagnostics.Process.GetProcessById(id);
                if (p.MainWindowHandle != 0) return p.MainWindowHandle;
            }
            catch (ArgumentException)
            {
                // gone meanwhile
            }
            catch (InvalidOperationException)
            {
            }
        }
        return 0;
    }

    public static void Flash()
    {
        var hwnd = Find();
        if (hwnd != 0) NativeMethods.Flash(hwnd);
    }

    public static void BringToFront()
    {
        var hwnd = Find();
        if (hwnd == 0) return;
        if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, 9); // SW_RESTORE
        NativeMethods.SetForegroundWindow(hwnd);
    }
}
