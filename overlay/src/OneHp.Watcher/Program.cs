using System.Diagnostics;
using AionMeter.Core.Capture;

namespace OneHp.Watcher;

/// <summary>
/// 1 HP: started at sign-in (HKCU Run) with the overlay's path. Every few seconds it looks for the AION 2 process and,
/// when the game starts, starts the overlay with --with-game (the overlay then closes by itself after the game).
/// No window, no network, no files written. It ends when the overlay asks (setting turned off) or the overlay is gone.
/// </summary>
internal static class Program
{
    // Shared with the overlay (Services/GameAutostart.cs and App.xaml.cs).
    private const string WatcherMutex = "1HP.Overlay.Watcher";
    private const string StopEventName = "1HP.Overlay.Watcher.Stop";
    private const string OverlayMutex = "AionMeter.SingleInstance";

    private static int Main(string[] args)
    {
        if (args.Length < 1) return 2;
        var overlay = args[0];
        using var single = new Mutex(true, WatcherMutex, out var first);
        if (!first) return 0;
        using var stop = new EventWaitHandle(false, EventResetMode.ManualReset, StopEventName);

        var gameWasRunning = GameRunning(); // signed in with the game already open: the user decides about the overlay
        while (!stop.WaitOne(TimeSpan.FromSeconds(4)))
        {
            if (!File.Exists(overlay)) return 0; // moved or deleted: the overlay registers its new place when started
            var running = GameRunning();
            if (running && !gameWasRunning && !OverlayRunning()) StartOverlay(overlay);
            gameWasRunning = running;
        }
        return 0;
    }

    private static bool GameRunning()
    {
        try
        {
            return GameProcessLocator.FindGameProcessIds().Length > 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool OverlayRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(OverlayMutex, out var mutex)) return false;
            mutex.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true; // the overlay runs elevated: it is there
        }
    }

    private static void StartOverlay(string overlay)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(overlay, "--with-game")
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(overlay) ?? "",
            });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
