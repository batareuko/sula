using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace AionMeter.App.Services;

/// <summary>
/// 1 HP: "start with AION 2". The small 1HP-Watcher.exe shipped next to the overlay is copied to
/// %LOCALAPPDATA%\1HP-Overlay (so the unpacked folder is never locked and a new version can be unpacked over it),
/// registered in HKCU\…\Run with the overlay's path, and started. It opens the overlay when the game starts; the
/// overlay closes itself after the game (App.CheckGameExit). Turning the setting off removes the entry and stops it.
/// </summary>
public static class GameAutostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "1HP-Overlay";
    private const string WatcherFile = "1HP-Watcher.exe";
    // Shared with src/OneHp.Watcher/Program.cs.
    private const string WatcherMutex = "1HP.Overlay.Watcher";
    private const string StopEventName = "1HP.Overlay.Watcher.Stop";

    private static string InstalledWatcher => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "1HP-Overlay", WatcherFile);

    public static void Apply(bool on)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (on) Enable();
            else Disable();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
                                       or Win32Exception or InvalidOperationException)
        {
            Log.Info("Start with the game: " + ex.Message);
        }
    }

    private static void Enable()
    {
        var overlay = Environment.ProcessPath;
        var bundled = Path.Combine(AppContext.BaseDirectory, WatcherFile);
        if (overlay is null || !File.Exists(bundled))
        {
            Log.Info("Start with the game: no " + WatcherFile + " next to the overlay (developer build)");
            return;
        }
        var watcher = InstalledWatcher;
        var command = $"\"{watcher}\" \"{overlay}\"";
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        var registered = key.GetValue(RunValue) as string;
        var fresh = !SameFile(bundled, watcher);

        // A watcher from another version or folder would start the wrong overlay: replace it.
        if ((fresh || registered != command) && WatcherRunning()) StopWatcher(wait: true);
        if (fresh)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(watcher)!);
            File.Copy(bundled, watcher, overwrite: true);
        }
        if (registered != command)
        {
            key.SetValue(RunValue, command);
            Log.Info("Start with the game: registered " + command);
        }
        if (!WatcherRunning())
            using (Process.Start(new ProcessStartInfo(watcher, $"\"{overlay}\"") { UseShellExecute = false })) { }
    }

    private static void Disable()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            if (key?.GetValue(RunValue) is not null)
            {
                key.DeleteValue(RunValue, throwOnMissingValue: false);
                Log.Info("Start with the game: turned off");
            }
        }
        if (WatcherRunning()) StopWatcher(wait: false);
    }

    private static bool SameFile(string a, string b)
    {
        if (!File.Exists(b)) return false;
        var x = new FileInfo(a);
        var y = new FileInfo(b);
        return x.Length == y.Length && x.LastWriteTimeUtc == y.LastWriteTimeUtc;
    }

    private static bool WatcherRunning()
    {
        try
        {
            if (!Mutex.TryOpenExisting(WatcherMutex, out var m)) return false;
            m.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static void StopWatcher(bool wait)
    {
        if (!EventWaitHandle.TryOpenExisting(StopEventName, out var stop)) return;
        using (stop) stop.Set();
        if (!wait) return;
        for (var i = 0; i < 30 && WatcherRunning(); i++) Thread.Sleep(100); // it checks every 4 s but wakes on the event at once
    }
}
