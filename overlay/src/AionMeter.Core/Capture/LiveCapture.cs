using System.Net;
using AionMeter.Core.Events;
using AionMeter.Core.Game;
using PacketDotNet;
using SharpPcap;
using SharpPcap.LibPcap;

namespace AionMeter.Core.Capture;

public sealed class LiveCaptureOptions
{
    /// <summary>Npcap device name to force (e.g. a VPN adapter). Null = pick automatically from the game's connections.</summary>
    public string? DeviceName { get; set; }

    /// <summary>When set, every captured game packet is also written to a .pcap file in this folder.</summary>
    public string? RecordDirectory { get; set; }
}

/// <summary>
/// Passive capture of the AION 2 connection through Npcap. It looks up the game's own TCP connections in the
/// Windows TCP table, opens only the adapter that carries them (the loopback adapter when a ping accelerator relays
/// the game locally) and filters to those servers. Nothing is ever sent or injected.
/// </summary>
public sealed class LiveCapture : IEventSource
{
    private readonly LiveCaptureOptions _options;
    private readonly PacketPipeline _pipeline;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _monitor;
    private LibPcapLiveDevice? _device;
    private string? _filter;
    private CaptureFileWriterDevice? _writer;
    private long _packets;
    private long _events;
    private CaptureStatus _status = new(CaptureState.Stopped, "Capture stopped");

    public LiveCapture(GameData data, LiveCaptureOptions options)
    {
        _options = options;
        _pipeline = new PacketPipeline(data, OnEvent) { LockThreshold = 3 };
    }

    public event Action<GameEvent>? EventDecoded;
    public event Action<CaptureStatus>? StatusChanged;

    public CaptureStatus Status => _status;
    public PacketPipeline Pipeline => _pipeline;
    public string? RecordingPath { get; private set; }

    /// <summary>Npcap's capture library is on this PC (its own folder, or System32 in WinPcap-compatible mode).</summary>
    public static bool IsNpcapInstalled() =>
        File.Exists(Path.Combine(Environment.SystemDirectory, "Npcap", "wpcap.dll")) ||
        File.Exists(Path.Combine(Environment.SystemDirectory, "wpcap.dll"));

    public static IReadOnlyList<(string Name, string Description)> ListDevices()
    {
        return CaptureDeviceList.Instance.OfType<LibPcapLiveDevice>()
            .Select(d => (d.Name, Describe(d)))
            .ToList();
    }

    public void Start()
    {
        if (_monitor is not null) return;
        _cts = new CancellationTokenSource();
        SetStatus(CaptureState.Starting, "Starting capture…");
        _monitor = Task.Run(() => MonitorLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _monitor?.Wait(2_000); } catch (AggregateException) { }
        _monitor = null;
        CloseDevice();
        SetStatus(CaptureState.Stopped, "Capture stopped");
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    private async Task MonitorLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Refresh();
            }
            catch (DllNotFoundException)
            {
                SetStatus(CaptureState.Error, "Npcap is not installed — get it from npcap.com");
            }
            catch (TypeInitializationException)
            {
                SetStatus(CaptureState.Error, "Npcap is not installed — get it from npcap.com");
            }
            catch (PcapException ex)
            {
                SetStatus(CaptureState.Error, "Capture error: " + ex.Message);
                CloseDevice();
            }
            try
            {
                await Task.Delay(2_000, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private const long QuietAfterMs = 5_000, RescanMs = 3_000;
    private long _lastScanMs;

    private void Refresh()
    {
        if (_options.DeviceName is { Length: > 0 } forced)
        {
            var dev = CaptureDeviceList.Instance.OfType<LibPcapLiveDevice>().FirstOrDefault(d => d.Name == forced);
            if (dev is null)
            {
                SetStatus(CaptureState.Error, "Selected network adapter not found");
                return;
            }
            EnsureDevice(dev, "tcp");
            ReportFlowStatus(dev);
            return;
        }

        // 1 HP quiet capture: while the game stream flows, the game is not looked at again; it is looked for only at the
        // start and when the stream went quiet (a teleport or a loading screen moves it to another server), at most every 3 s.
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (_device is { } open)
        {
            if (_pipeline.GameFlows > 0 && now - _pipeline.LastGameDataMs < QuietAfterMs)
            {
                ReportFlowStatus(open);
                return;
            }
            if (now - _lastScanMs < RescanMs)
            {
                ReportFlowStatus(open);
                return;
            }
        }
        _lastScanMs = now;

        var pids = GameProcessLocator.FindGameProcessIds();
        if (pids.Length == 0)
        {
            CloseDevice();
            SetStatus(CaptureState.WaitingForGame, "AION 2 is not running");
            return;
        }

        var connections = GameProcessLocator.FindConnections(pids);
        if (connections.Count == 0)
        {
            // between connections (character select): hear everything, so the next one is caught from its first packet
            lock (_gate) if (_device is { } between && _filter != "tcp") { between.Filter = "tcp"; _filter = "tcp"; }
            SetStatus(CaptureState.WaitingForGame, "Waiting for the game to connect…");
            return;
        }

        var devices = CaptureDeviceList.Instance.OfType<LibPcapLiveDevice>().ToList();
        LibPcapLiveDevice? device = null;
        foreach (var c in connections.OrderByDescending(c => c.RemotePort == PacketPipeline.GamePort))
        {
            device = IPAddress.IsLoopback(c.RemoteIp) || IPAddress.IsLoopback(c.LocalIp)
                ? devices.FirstOrDefault(IsLoopback)
                : devices.FirstOrDefault(d => d.Addresses.Any(a => c.LocalIp.Equals(a.Addr?.ipAddress)));
            if (device is not null) break;
        }
        if (device is null)
        {
            SetStatus(CaptureState.Error, "No Npcap adapter carries the game connection (VPN? pick one in Settings)");
            return;
        }

        // 1 HP: while the game stream is quiet (character select, a loading screen) every TCP packet on this adapter is
        // heard, so a new connection — to another port, another server or through a local proxy (ExitLag …) — is caught
        // from its first packet: that login burst carries your character and the map. Once game data flows again the
        // filter narrows back to the game's connections.
        var quiet = _pipeline.GameFlows == 0 || now - _pipeline.LastGameDataMs >= QuietAfterMs;
        var hosts = connections.Select(c => $"(host {c.RemoteIp} and port {c.RemotePort})").Distinct().OrderBy(s => s);
        EnsureDevice(device, quiet ? "tcp" : "tcp and (" + string.Join(" or ", hosts) + ")");
        ReportFlowStatus(device);
    }

    private void ReportFlowStatus(LibPcapLiveDevice device)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var flows = _pipeline.GameFlows;
        if (flows > 0 && now - _pipeline.LastGameDataMs < 10_000)
        {
            var server = _pipeline.LockedFlows.Select(f => f.Src.ToString()).FirstOrDefault();
            SetStatus(CaptureState.Capturing, $"Capturing · {server}" + (RecordingPath is null ? "" : " · REC"));
        }
        else if (flows > 0)
        {
            SetStatus(CaptureState.WaitingForGame, "Connected · no game traffic for a while");
        }
        else
        {
            SetStatus(CaptureState.WaitingForGame, $"Listening on {Describe(device)}…");
        }
    }

    private void EnsureDevice(LibPcapLiveDevice device, string filter)
    {
        lock (_gate)
        {
            if (_device is not null && _device.Name != device.Name) CloseDeviceLocked();
            if (_device is null)
            {
                device.OnPacketArrival += OnPacketArrival;
                device.Open(new DeviceConfiguration { Mode = DeviceModes.None, ReadTimeout = 100 });
                device.Filter = filter;
                _filter = filter;
                _device = device;
                OpenRecorder(device);
                device.StartCapture();
            }
            else if (_filter != filter)
            {
                _device.Filter = filter;
                _filter = filter;
            }
        }
    }

    private void OpenRecorder(LibPcapLiveDevice device)
    {
        if (_analysisDir is { } analysis) OpenRecorder(device, analysis, "analysis");
        else if (_options.RecordDirectory is { Length: > 0 } dir) OpenRecorder(device, dir, "aion2");
    }

    private readonly object _recordGate = new();
    private volatile string? _analysisDir;

    private void OpenRecorder(LibPcapLiveDevice device, string dir, string prefix)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.pcap");
        var writer = new CaptureFileWriterDevice(path);
        writer.Open(new DeviceConfiguration { LinkLayerType = device.LinkType });
        lock (_recordGate)
        {
            _writer?.Close();
            _writer = writer;
            RecordingPath = path;
        }
    }

    /// <summary>
    /// 1 HP: the "record for analysis" button — the game's packets go to pcap files in <paramref name="dir"/> until
    /// <see cref="StopRecording"/>, also across a game restart or an adapter change (each opening starts a new file).
    /// A recording made because of Settings → "record packets" is closed and the analysis takes over.
    /// </summary>
    public void StartRecording(string dir)
    {
        lock (_gate)
        {
            _analysisDir = dir;
            if (_device is not null) OpenRecorder(_device, dir, "analysis");
        }
    }

    /// <summary>1 HP: ends the analysis recording (packets written so far stay in its folder).</summary>
    public void StopRecording()
    {
        lock (_gate)
        {
            if (_analysisDir is null) return;
            _analysisDir = null;
            lock (_recordGate)
            {
                _writer?.Close();
                _writer = null;
                RecordingPath = null;
            }
        }
    }

    private void CloseDevice()
    {
        lock (_gate) CloseDeviceLocked();
    }

    private void CloseDeviceLocked()
    {
        if (_device is null) return;
        try
        {
            _device.StopCapture();
        }
        catch (PcapException)
        {
        }
        _device.OnPacketArrival -= OnPacketArrival;
        _device.Close();
        _device = null;
        _filter = null;
        lock (_recordGate)
        {
            _writer?.Close();
            _writer = null;
            RecordingPath = null;
        }
    }

    private void OnPacketArrival(object sender, PacketCapture e)
    {
        var raw = e.GetPacket();
        Interlocked.Increment(ref _packets);
        try
        {
            if (_writer is not null)
                lock (_recordGate)
                    _writer?.Write(raw);
            if (SegmentReader.TryRead(raw, out var seg))
                _pipeline.OnSegment(seg.TimeMs, seg.Key, seg.Seq, seg.Syn, seg.FinOrRst, seg.Payload);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A malformed packet must never take the capture thread down.
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private void OnEvent(GameEvent e)
    {
        _events++;
        EventDecoded?.Invoke(e);
    }

    private void SetStatus(CaptureState state, string message)
    {
        var s = new CaptureStatus(state, message, Interlocked.Read(ref _packets), _events);
        _status = s;
        StatusChanged?.Invoke(s);
    }

    private static bool IsLoopback(LibPcapLiveDevice d) =>
        d.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
        (d.Description?.Contains("loopback", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string Describe(LibPcapLiveDevice d) =>
        IsLoopback(d) ? "Loopback" : string.IsNullOrWhiteSpace(d.Description) ? d.Name : d.Description!;
}

/// <summary>Link layer → TCP segment, shared by live capture and pcap replay.</summary>
public static class SegmentReader
{
    public readonly record struct Segment(long TimeMs, FlowKey Key, uint Seq, bool Syn, bool FinOrRst, byte[] Payload);

    public static bool TryRead(RawCapture raw, out Segment segment)
    {
        segment = default;
        Packet packet;
        try
        {
            packet = Packet.ParsePacket(raw.LinkLayerType, raw.Data);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or NotImplementedException)
        {
            return false;
        }
        var ip = packet.Extract<IPPacket>();
        var tcp = packet.Extract<TcpPacket>();
        if (ip is null || tcp is null) return false;

        var t = raw.Timeval;
        var ms = (long)t.Seconds * 1000 + (long)t.MicroSeconds / 1000;
        var key = new FlowKey(ip.SourceAddress, tcp.SourcePort, ip.DestinationAddress, tcp.DestinationPort);
        segment = new Segment(ms, key, tcp.SequenceNumber, tcp.Synchronize, tcp.Finished || tcp.Reset, tcp.PayloadData ?? []);
        return true;
    }
}
