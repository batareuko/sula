using System.IO.Compression;
using AionMeter.Core.Updates;

namespace AionMeter.Tests;

/// <summary>1 HP: an unzipped overlay replaces its own files from a release zip, or stays exactly as it was.</summary>
public sealed class PortableInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "onehp-install-" + Guid.NewGuid().ToString("N"));
    private string AppDir => Path.Combine(_root, "app");
    private string WorkDir => Path.Combine(_root, "work");
    private readonly List<string> _log = new();

    public PortableInstallTests()
    {
        Directory.CreateDirectory(AppDir);
        Directory.CreateDirectory(WorkDir);
        Write(AppDir, "1HP-Overlay.exe", "old exe");
        Write(AppDir, "1HP-Overlay.dll", "old dll");
        Write(AppDir, "data/npcs/en.json", "old npcs");
        Write(AppDir, "only-in-old.txt", "keep me");
    }

    private static void Write(string dir, string relative, string text)
    {
        var path = Path.Combine(dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private string Read(string relative) => File.ReadAllText(Path.Combine(AppDir, relative));

    private string Zip(params (string Name, string Text)[] files)
    {
        var path = Path.Combine(WorkDir, "release.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, text) in files)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open());
            w.Write(text);
        }
        return path;
    }

    private bool Apply(string zip) => PortableInstall.Apply(zip, AppDir, "1HP-Overlay.exe", WorkDir, _log.Add, retries: 0, retryDelayMs: 0);

    [Fact]
    public void Release_files_replace_the_old_ones_and_nothing_else_changes()
    {
        var zip = Zip(("1HP-Overlay.exe", "new exe"), ("1HP-Overlay.dll", "new dll"), ("data/npcs/en.json", "new npcs"),
            ("1HP-Watcher.exe", "watcher"));

        Assert.True(Apply(zip));
        Assert.Equal("new exe", Read("1HP-Overlay.exe"));
        Assert.Equal("new dll", Read("1HP-Overlay.dll"));
        Assert.Equal("new npcs", Read("data/npcs/en.json"));
        Assert.Equal("watcher", Read("1HP-Watcher.exe"));
        Assert.Equal("keep me", Read("only-in-old.txt"));
        Assert.Equal(["release.zip"], Directory.EnumerateFileSystemEntries(WorkDir).Select(Path.GetFileName)); // staging and backup gone
    }

    [Fact]
    public void A_zip_without_the_overlay_is_refused()
    {
        Assert.False(Apply(Zip(("readme.txt", "hello"))));
        Assert.Equal("old exe", Read("1HP-Overlay.exe"));
        Assert.False(File.Exists(Path.Combine(AppDir, "readme.txt")));
    }

    [Fact]
    public void A_failure_part_way_puts_the_old_version_back()
    {
        // A folder where the release has a file: copying it fails after (or before) the other files went in.
        Directory.CreateDirectory(Path.Combine(AppDir, "blocked.dll"));
        var zip = Zip(("1HP-Overlay.exe", "new exe"), ("1HP-Overlay.dll", "new dll"), ("data/npcs/en.json", "new npcs"),
            ("blocked.dll", "x"), ("new-file.txt", "new"));

        Assert.False(Apply(zip));
        Assert.Equal("old exe", Read("1HP-Overlay.exe"));
        Assert.Equal("old dll", Read("1HP-Overlay.dll"));
        Assert.Equal("old npcs", Read("data/npcs/en.json"));
        Assert.False(File.Exists(Path.Combine(AppDir, "new-file.txt")));
        Assert.Contains(_log, l => l.StartsWith("Update failed", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
