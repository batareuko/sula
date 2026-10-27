using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AionMeter.Core.Capture;
using AionMeter.Core.Events;

namespace AionMeter.App.Services;

/// <summary>Where a finished analysis recording ended up: a link to paste in the chat, or the zip on the Desktop.</summary>
public sealed record AnalysisResult(string? Link, string ZipPath, long Bytes, string? Error);

/// <summary>
/// 1 HP: the "record for analysis" button. Between Start and Stop it keeps the game's packets (pcap, from the live
/// capture) and every event the meter decoded (events.jsonl); Stop packs them with a short summary and today's log
/// into one zip, uploads it with the member's 1 HP key and returns a 7-day link. Without a key (or offline) the zip
/// is put on the Desktop instead. Stops by itself after <see cref="MaxMinutes"/> minutes or when the packets get big.
/// </summary>
public sealed class AnalysisRecorder
{
    public const int MaxMinutes = 20;
    private const long MaxPcapBytes = 140L * 1024 * 1024; // zipped that stays under the 50 MB upload limit
    private const int MaxEvents = 500_000;

    private static readonly JsonSerializerOptions Json = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();
    private StreamWriter? _events;
    private int _eventCount;
    private readonly Dictionary<string, int> _counts = new();

    public string? Folder { get; private set; }
    public DateTime StartedAt { get; private set; }
    public bool Running => Folder is not null;
    public int EventCount => _eventCount;

    public static string Root => Path.Combine(AppSettings.AppDataDir, "analysis");

    public void Start(LiveCapture? capture)
    {
        lock (_gate)
        {
            if (Folder is not null) return;
            var folder = Path.Combine(Root, $"1hp_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(folder);
            _events = new StreamWriter(Path.Combine(folder, "events.jsonl"), false, new UTF8Encoding(false)) { AutoFlush = false };
            _eventCount = 0;
            _counts.Clear();
            StartedAt = DateTime.Now;
            Folder = folder;
        }
        capture?.StartRecording(Folder);
        Log.Info("Analysis recording started: " + Folder);
    }

    /// <summary>The capture was recreated (restart button, adapter change): the new one keeps recording into the same folder.</summary>
    public void Attach(LiveCapture? capture)
    {
        if (Folder is { } folder) capture?.StartRecording(folder);
    }

    /// <summary>Every decoded event, as one JSON line: {"t":ms,"type":"DamageEvent",...}.</summary>
    public void Write(GameEvent e)
    {
        if (_events is null) return;
        lock (_gate)
        {
            if (_events is null || _eventCount >= MaxEvents) return;
            var type = e.GetType().Name;
            _counts[type] = _counts.GetValueOrDefault(type) + 1;
            string body;
            try { body = JsonSerializer.Serialize(e, e.GetType(), Json); }
            catch (Exception ex) { body = JsonSerializer.Serialize(new { e.TimeMs, error = ex.Message }); }
            _events.Write("{\"type\":\"");
            _events.Write(type);
            _events.Write("\",");
            _events.WriteLine(body.Length > 2 ? body[1..] : "}");
            _eventCount++;
        }
    }

    /// <summary>Time to stop by itself: too long, or the packets are about as much as one upload takes.</summary>
    public bool ShouldAutoStop()
    {
        if (Folder is not { } folder) return false;
        if (DateTime.Now - StartedAt > TimeSpan.FromMinutes(MaxMinutes)) return true;
        try { return Directory.GetFiles(folder, "*.pcap").Sum(f => new FileInfo(f).Length) > MaxPcapBytes; }
        catch (IOException) { return false; }
    }

    /// <summary>Ends the recording and writes the zip (summary first). Returns null when nothing was running.</summary>
    public string? Stop(LiveCapture? capture, object summary)
    {
        string folder;
        lock (_gate)
        {
            if (Folder is null) return null;
            folder = Folder;
            _events?.Flush();
            _events?.Dispose();
            _events = null;
            Folder = null;
        }
        capture?.StopRecording();

        var info = new Dictionary<string, object?>
        {
            ["app"] = AppInfo.Name,
            ["version"] = typeof(AnalysisRecorder).Assembly.GetName().Version?.ToString(3),
            ["started"] = StartedAt.ToString("O"),
            ["stopped"] = DateTime.Now.ToString("O"),
            ["timeZone"] = TimeZoneInfo.Local.Id,
            ["events"] = _eventCount,
            ["eventTypes"] = _counts.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value),
            ["meter"] = summary,
        };
        File.WriteAllText(Path.Combine(folder, "summary.json"), JsonSerializer.Serialize(info, new JsonSerializerOptions(Json) { WriteIndented = true }));
        CopyLogs(folder);

        var zip = folder + ".zip";
        if (File.Exists(zip)) File.Delete(zip);
        ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.SmallestSize, includeBaseDirectory: false);
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        Log.Info($"Analysis recording stopped: {zip} ({new FileInfo(zip).Length / 1024} KB, {_eventCount} events)");
        return zip;
    }

    /// <summary>1 HP: the last day's logs alone, zipped (tray → send the log), for disconnects and other trouble.</summary>
    public static string PackLogs()
    {
        var name = "logs-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var folder = Path.Combine(Root, name);
        Directory.CreateDirectory(folder);
        CopyLogs(folder, hours: 30, files: 6);
        var zip = Path.Combine(Root, name + ".zip");
        ZipFile.CreateFromDirectory(folder, zip, CompressionLevel.SmallestSize, includeBaseDirectory: false);
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        return zip;
    }

    /// <summary>Today's log (and the boss list log): what the meter thought while the packets came in.</summary>
    private static void CopyLogs(string folder, int hours = 12, int files = 3)
    {
        try
        {
            foreach (var file in Directory.GetFiles(Log.Directory)
                         .Where(f => File.GetLastWriteTime(f) > DateTime.Now.AddHours(-hours))
                         .OrderByDescending(File.GetLastWriteTime).Take(files))
            {
                // The log is still open for writing: read it shared instead of File.Copy.
                using var src = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var dst = File.Create(Path.Combine(folder, "log_" + Path.GetFileName(file)));
                src.CopyTo(dst);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info("Analysis: logs not copied: " + ex.Message);
        }
    }

    /// <summary>The zip on the Desktop, selected in Explorer: the fallback when it could not be uploaded.</summary>
    public static string ToDesktop(string zip)
    {
        var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Path.GetFileName(zip));
        try
        {
            File.Copy(zip, target, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info("Analysis: not copied to the Desktop: " + ex.Message);
            target = zip;
        }
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{target}\""); }
        catch (Exception ex) { Log.Info("Analysis: Explorer not opened: " + ex.Message); }
        return target;
    }
}
