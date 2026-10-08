using System.Diagnostics;
using AionMeter.Core.Capture;
using AionMeter.Core.Updates;

namespace OneHp.Watcher;

/// <summary>
/// 1 HP: started at sign-in (HKCU Run) with the overlay's path. Every few seconds it looks for the AION 2 process and,
/// when the game starts, starts the overlay with --with-game (the overlay then closes by itself after the game).
/// No window, no network, no files written while watching. It ends when the overlay asks (setting turned off) or the overlay is gone.
/// Started as <c>--apply &lt;zip&gt; &lt;overlay folder&gt; &lt;pid&gt; &lt;exe name&gt; restart|stay-closed</c> (from a copy in the temp folder) it
/// installs an update instead: waits for that overlay process to exit, replaces its files and starts it again.
/// </summary>
internal static class Program
{
    // Shared with the overlay (Services/GameAutostart.cs and App.xaml.cs).
    private const string WatcherMutex = "1HP.Overlay.Watcher";
    private const string StopEventName = "1HP.Overlay.Watcher.Stop";
    private const string OverlayMutex = "AionMeter.SingleInstance";

    private static int Main(string[] args)
    {
        if (args is ["--apply", var zip, var appDir, var pid, var exeName, var mode]) return ApplyUpdate(zip, appDir, pid, exeName, mode == "restart");
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
            if (running && !gameWasRunning && !OverlayRunning()) StartOverlay(overlay, "--with-game");
            gameWasRunning = running;
        }
        return 0;
    }

    private static int ApplyUpdate(string zip, string appDir, string pidText, string exeName, bool restart)
    {
        var logFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "1HP-Overlay", "update.log");
        void Log(string line)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
                File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            }
            catch (IOException)
            {
            }
        }

        Log($"Update from {zip} into {appDir}");
        if (int.TryParse(pidText, out var pid))
        {
            try
            {
                using var overlay = Process.GetProcessById(pid);
                if (!overlay.WaitForExit(60_000))
                {
                    Log("The overlay did not exit within a minute: update left for the next time");
                    return 1;
                }
            }
            catch (ArgumentException)
            {
                // already gone
            }
        }
        var ok = PortableInstall.Apply(zip, appDir, exeName, Path.GetDirectoryName(zip) ?? Path.GetTempPath(), Log);
        if (ok)
        {
            try
            {
                File.Delete(zip);
            }
            catch (IOException)
            {
            }
        }
        // The new version, or the old one again when the update could not go in: the player is never left without it.
        // An automatic update after the game was closed leaves it closed: the watcher starts it with the next game.
        if (restart || !ok) StartOverlay(Path.Combine(appDir, exeName), ok ? "--updated" : "");
        return ok ? 0 : 1;
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

    private static void StartOverlay(string overlay, string arguments)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(overlay, arguments)
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
