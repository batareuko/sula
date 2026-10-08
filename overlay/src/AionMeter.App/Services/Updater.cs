using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using AionMeter.Core.Updates;
using Microsoft.Win32;

namespace AionMeter.App.Services;

public enum UpdateCheckResult
{
    UpToDate,
    Available,
    Failed,
}

/// <summary>
/// New versions of the meter. Asks GitHub shortly after start and every few hours (Settings → Updates) and raises
/// <see cref="Changed"/> for the tray, the overlay and the update window. A copy installed by the installer updates
/// itself: it downloads the new installer in the background (<see cref="Downloaded"/>), and the app runs it at a quiet
/// moment without any window; the installer replaces this copy and starts the new one.
/// 1 HP: an unzipped copy updates itself the same way when 1HP-Watcher.exe sits next to it and its folder is writable:
/// it downloads the release zip (checked against GitHub's size and SHA-256), and at the quiet moment a temporary copy
/// of the watcher (--apply) waits for the meter to exit, swaps the files (rolling back on any failure) and starts it
/// again. Other copies get the download page.
/// </summary>
public sealed class Updater : IDisposable
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(2);
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{AE1B2590-0A93-4500-9CB1-0F543771957A}_is1"; // AppId in installer/AION2DpsMeter.iss

    private readonly AppSettings _settings;
    private readonly UpdateFeed _feed;
    private DispatcherTimer? _timer;
    private Task<UpdateCheckResult>? _running;

    public Updater(AppSettings settings)
    {
        _settings = settings;
        Current = UpdateFeed.Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0, 0));
        _feed = new UpdateFeed($"1HP-Overlay/{Current.ToString(3)}");
    }

    /// <summary>This copy's version (major.minor.patch).</summary>
    public Version Current { get; }

    /// <summary>The newest release GitHub reported, newer or not; null before the first successful check.</summary>
    public ReleaseInfo? Latest { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>A release newer than this copy, unless the user chose to skip that version.</summary>
    public ReleaseInfo? Available =>
        Latest is { } r && r.Version > Current && r.Version.ToString(3) != _settings.SkippedUpdate ? r : null;

    /// <summary>True when this copy runs from the folder the installer put it in (it can update itself).</summary>
    public static bool IsInstalled { get; } = DetectInstalled();

    /// <summary>1 HP: an unzipped copy that can replace its own files (1HP-Watcher.exe next to it, folder writable).</summary>
    public static bool CanUpdateZip { get; } = !IsInstalled && DetectZipUpdate();

    /// <summary>This copy can install a new version by itself, one way or the other.</summary>
    public static bool CanSelfUpdate => IsInstalled || CanUpdateZip;

    /// <summary>This copy fetches and installs new versions by itself (unless turned off).</summary>
    public bool AutoInstall => CanSelfUpdate && _settings.AutoInstallUpdates;

    /// <summary>The release file this copy installs from: the installer, or the zip for an unzipped copy.</summary>
    public static ReleaseAsset? UpdateAsset(ReleaseInfo release) =>
        IsInstalled ? release.Installer : CanUpdateZip ? release.Portable : null;

    /// <summary>The update whose installer is downloaded and checked, waiting for a quiet moment to be installed.</summary>
    public ReleaseInfo? Downloaded { get; private set; }

    /// <summary>The background download of <see cref="Available"/> failed: fall back to asking the player.</summary>
    public bool DownloadFailed { get; private set; }

    private string? _downloadedPath;
    private bool _downloading;

    /// <summary>Raised on the UI thread after every check and when a version is skipped.</summary>
    public event Action? Changed;

    /// <summary>Automatic checks: the first shortly after start, then every few hours, while the setting is on.</summary>
    public void Start()
    {
        if (_timer is not null) return;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = FirstCheckDelay };
        _timer.Tick += (_, _) =>
        {
            _timer.Interval = CheckInterval;
            if (_settings.CheckUpdates) _ = CheckAsync();
        };
        _timer.Start();
    }

    /// <summary>Asks GitHub now. <see cref="UpdateCheckResult.Available"/> also for a skipped version (the user asked).</summary>
    public async Task<UpdateCheckResult> CheckAsync()
    {
        if (_running is { } running) return await running; // a check is already on its way: share its answer
        _running = RunCheckAsync();
        try
        {
            return await _running;
        }
        finally
        {
            _running = null;
        }
    }

    private async Task<UpdateCheckResult> RunCheckAsync()
    {
        try
        {
            Latest = await _feed.GetLatestAsync();
            LastError = null;
            Log.Info($"Update check: newest release {Latest?.Tag ?? "none"}, this copy {Current.ToString(3)}.");
            if (Available is { } update && UpdateAsset(update) is not null && AutoInstall) _ = PrepareAsync(update);
            return Latest is { } r && r.Version > Current ? UpdateCheckResult.Available : UpdateCheckResult.UpToDate;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            LastError = ex is TaskCanceledException ? "timeout" : ex.Message;
            Log.Info($"Update check failed: {LastError}");
            return UpdateCheckResult.Failed;
        }
        finally
        {
            Changed?.Invoke();
        }
    }

    /// <summary>Stop announcing this version; a newer one is announced again.</summary>
    public void Skip(ReleaseInfo release)
    {
        _settings.SkippedUpdate = release.Version.ToString(3);
        _settings.Save();
        Changed?.Invoke();
    }

    /// <summary>Downloads the release's installer (or zip) to the temp folder, checked against GitHub's size and SHA-256.</summary>
    public Task<string> DownloadUpdateAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken ct) =>
        UpdateAsset(release) is { } asset
            ? _feed.DownloadAsync(asset, DownloadFolder, progress, ct)
            : throw new InvalidOperationException($"Release {release.Tag} has nothing this copy can install");

    /// <summary>Fetches the update in the background, so it can go in at the next quiet moment.</summary>
    private async Task PrepareAsync(ReleaseInfo release)
    {
        if (_downloading || Downloaded?.Version == release.Version) return;
        _downloading = true;
        DownloadFailed = false;
        try
        {
            _downloadedPath = await DownloadUpdateAsync(release, null, CancellationToken.None);
            Downloaded = release;
            Log.Info($"Update {release.Tag} downloaded: {_downloadedPath}");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or InvalidOperationException
                                       or OperationCanceledException)
        {
            DownloadFailed = true;
            Log.Info($"Update {release.Tag} not downloaded: {ex.Message}");
        }
        finally
        {
            _downloading = false;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Installs the downloaded update without any window; the caller exits right after. <paramref name="restart"/>:
    /// start the meter again afterwards (an unzipped copy can also stay closed until the game starts it).
    /// </summary>
    public void InstallDownloaded(bool restart = true)
    {
        if (_downloadedPath is null) throw new InvalidOperationException("No update downloaded");
        Install(_downloadedPath, quiet: true, restart);
    }

    /// <summary>Starts installing a downloaded installer or release zip; the caller exits right after.</summary>
    public static void Install(string path, bool quiet, bool restart = true)
    {
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) LaunchZipUpdate(path, restart);
        else LaunchInstaller(path, quiet);
    }

    private const string WatcherFile = "1HP-Watcher.exe";

    /// <summary>
    /// 1 HP: a temporary copy of 1HP-Watcher.exe replaces this unzipped copy's files once it has exited (see
    /// Core/Updates/PortableInstall.cs). The copy runs from the temp folder, so nothing in the overlay folder is in use.
    /// </summary>
    private static void LaunchZipUpdate(string zip, bool restart)
    {
        var appDir = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var exe = Path.GetFileName(Environment.ProcessPath) ?? "1HP-Overlay.exe";
        Directory.CreateDirectory(DownloadFolder);
        var updater = Path.Combine(DownloadFolder, $"1HP-Updater-{Guid.NewGuid().ToString("N")[..8]}.exe");
        File.Copy(Path.Combine(appDir, WatcherFile), updater, overwrite: true);
        var start = new ProcessStartInfo(updater) { UseShellExecute = false };
        foreach (var arg in new[] { "--apply", zip, appDir, Environment.ProcessId.ToString(), exe, restart ? "restart" : "stay-closed" })
            start.ArgumentList.Add(arg);
        using (Process.Start(start)) { }
        Log.Info($"Zip update handed to {updater} (restart: {restart})");
    }

    private static bool DetectZipUpdate()
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            if (!File.Exists(Path.Combine(dir, WatcherFile))) return false;
            var probe = Path.Combine(dir, ".write-test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // e.g. unzipped into Program Files: it cannot change its own files
        }
    }

    /// <summary>
    /// Starts the downloaded installer: <paramref name="quiet"/> with no window at all (automatic updates), otherwise
    /// with a small progress window (the player clicked Update). It closes this copy if it is still running (Restart
    /// Manager), installs over it and starts the new version (<c>/RELAUNCH=1</c>, see the [Run] section of the
    /// installer). The caller exits right after.
    /// </summary>
    public static void LaunchInstaller(string path, bool quiet = false)
    {
        if (!UpdateFeed.IsProductName(FileVersionInfo.GetVersionInfo(path).ProductName, AppInfo.Name))
            throw new InvalidDataException($"{Path.GetFileName(path)} is not the {AppInfo.Name} installer");
        Process.Start(new ProcessStartInfo(path, (quiet ? "/VERYSILENT" : "/SILENT") + " /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1")
            { UseShellExecute = true });
    }

    public static void OpenReleasePage(ReleaseInfo? release)
    {
        try
        {
            Process.Start(new ProcessStartInfo(release?.PageUrl ?? UpdateFeed.ReleasesPage) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error("Could not open the release page", ex);
        }
    }

    private static string DownloadFolder => Path.Combine(Path.GetTempPath(), "1HP-Overlay-update");

    /// <summary>Installers of earlier updates are no longer needed (the one running right now is locked and stays).</summary>
    public static void CleanDownloads()
    {
        try
        {
            if (!Directory.Exists(DownloadFolder)) return;
            foreach (var file in Directory.EnumerateFiles(DownloadFolder))
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > TimeSpan.FromHours(1))
                    File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>The render tool shows the update window and the overlay's update button with a made-up release.</summary>
    internal void Preview(ReleaseInfo release) => Latest = release;

    private static bool DetectInstalled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key?.GetValue("InstallLocation") is string location && location.Length > 0 &&
                   string.Equals(Path.GetFullPath(location).TrimEnd('\\'), Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _timer?.Stop();
        _feed.Dispose();
    }
}
