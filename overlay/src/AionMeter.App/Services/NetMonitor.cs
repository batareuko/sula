using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AionMeter.Core.Capture;
using AionMeter.Core.OneHp;

namespace AionMeter.App.Services;

/// <summary>
/// 1 HP: live network health for the overlay strip. Once a second it pings the game server and the home router
/// (ICMP, from this PC only — nothing goes to the game connection itself) and samples the loss counters of the
/// captured game stream. The game server often ignores ping: then the stream counters are the only loss figure,
/// and they are the better one anyway (they count the game's own lost packets).
/// </summary>
public sealed class NetMonitor : IDisposable
{
    private readonly Func<StreamCounters?> _stream;
    private readonly NetStats _server = new();
    private readonly NetStats _gateway = new();
    private readonly StreamLossWindow _loss = new();
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public NetMonitor(Func<StreamCounters?> stream) => _stream = stream;

    public IPAddress? Server { get; private set; }
    public IPAddress? Gateway { get; private set; }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => Loop(_cts.Token));
    }

    public NetReport Report()
    {
        (long Segments, long Holes, double? LossPct) stream;
        lock (_gate) stream = _loss.Current();
        var server = _server.Summary();
        return new NetReport(Server, server, Gateway, _gateway.Summary(), stream.Segments, stream.Holes, stream.LossPct,
            NetGrade.Of(stream.LossPct, server));
    }

    private async Task Loop(CancellationToken ct)
    {
        using var serverPing = new Ping();
        using var gatewayPing = new Ping();
        var tick = 0;
        while (!ct.IsCancellationRequested)
        {
            var started = Environment.TickCount64;
            try
            {
                if (tick++ % 10 == 0) FindTargets();
                var now = Environment.TickCount64;

                var tasks = new List<Task>();
                if (Server is { } s) tasks.Add(Probe(serverPing, s, _server, now));
                if (Gateway is { } g) tasks.Add(Probe(gatewayPing, g, _gateway, now));
                await Task.WhenAll(tasks).ConfigureAwait(false);

                if (_stream() is { } c)
                    lock (_gate) _loss.Add(now, c.Segments, c.Holes);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Info("Net monitor: " + ex.Message);
            }
            var wait = 1_000 - (int)(Environment.TickCount64 - started);
            try { await Task.Delay(Math.Max(100, wait), ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
    }

    private static async Task Probe(Ping ping, IPAddress ip, NetStats stats, long now)
    {
        int? rtt = null;
        try
        {
            var reply = await ping.SendPingAsync(ip, 1_000).ConfigureAwait(false);
            if (reply.Status == IPStatus.Success) rtt = (int)reply.RoundtripTime;
        }
        catch (PingException) { }
        catch (InvalidOperationException) { } // the previous request is still pending
        stats.Add(now, rtt);
    }

    /// <summary>The game server = the remote address most of the game's TCP connections go to; the router = default gateway.</summary>
    private void FindTargets()
    {
        IPAddress? server = null;
        if (OperatingSystem.IsWindows())
        {
            var pids = GameProcessLocator.FindGameProcessIds();
            var conns = GameProcessLocator.FindConnections(pids);
            server = conns.GroupBy(c => c.RemoteIp).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
            // A ping accelerator relays the game through a local proxy: pinging 127.0.0.1 tells nothing.
            if (server is not null && IPAddress.IsLoopback(server)) server = null;
        }
        if (!Equals(server, Server))
        {
            Server = server;
            _server.Clear();
        }

        var gateway = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().GatewayAddresses)
            .Select(g => g.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
        if (!Equals(gateway, Gateway))
        {
            Gateway = gateway;
            _gateway.Clear();
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _loop?.Wait(1_500); } catch (AggregateException) { }
        _cts?.Dispose();
    }
}

public sealed record NetReport(
    IPAddress? Server, NetSummary ServerPing, IPAddress? Gateway, NetSummary GatewayPing,
    long StreamSegments, long StreamHoles, double? StreamLossPct, NetQuality Quality);
