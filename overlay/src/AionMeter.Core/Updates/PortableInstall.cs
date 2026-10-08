using System.IO.Compression;

namespace AionMeter.Core.Updates;

/// <summary>
/// 1 HP: updates an unzipped (portable) copy in place. Used by 1HP-Watcher.exe in its --apply mode once the overlay has
/// exited: the release zip is unpacked aside, checked to hold the overlay, then each file replaces the old one; the old
/// files are moved to a backup first, so any failure puts the previous version back as it was. Files the new version
/// no longer has are left alone, and nothing outside the overlay folder is touched (settings and history live in
/// %APPDATA%\1HP-Overlay).
/// </summary>
public static class PortableInstall
{
    /// <summary>True when <paramref name="appDir"/> now holds the new version; false when it was left as it was.</summary>
    public static bool Apply(string zip, string appDir, string exeName, string workDir, Action<string> log,
        int retries = 20, int retryDelayMs = 500)
    {
        var staging = Path.Combine(workDir, "staging-" + Guid.NewGuid().ToString("N"));
        var backup = Path.Combine(workDir, "backup-" + Guid.NewGuid().ToString("N"));
        var replaced = new List<(string Target, string? Backup)>();
        var keepBackup = false;
        try
        {
            ZipFile.ExtractToDirectory(zip, staging); // refuses entries that would land outside staging
            if (!File.Exists(Path.Combine(staging, exeName)))
            {
                log($"{Path.GetFileName(zip)} has no {exeName}: not an overlay release");
                return false;
            }

            foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(staging, file);
                var target = Path.Combine(appDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                string? saved = null;
                if (File.Exists(target))
                {
                    saved = Path.Combine(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    var from = target;
                    Retry(() => File.Move(from, saved), retries, retryDelayMs); // antivirus or a slow exit may hold it a moment
                }
                replaced.Add((target, saved));
                File.Copy(file, target);
            }
            log($"Updated {replaced.Count} files in {appDir}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
        {
            log($"Update failed, restoring the previous version: {ex.Message}");
            for (var i = replaced.Count - 1; i >= 0; i--)
            {
                var (target, saved) = replaced[i];
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    if (saved is not null) File.Move(saved, target);
                }
                catch (Exception undo) when (undo is IOException or UnauthorizedAccessException)
                {
                    log($"Could not restore {target}: {undo.Message} (the old file stays in {backup})");
                    keepBackup = true;
                }
            }
            return false;
        }
        finally
        {
            TryDelete(staging);
            if (!keepBackup) TryDelete(backup);
        }
    }

    private static void Retry(Action action, int retries, int delayMs)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < retries)
            {
                Thread.Sleep(delayMs);
            }
        }
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
