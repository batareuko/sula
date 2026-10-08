namespace AionMeter.App.Services;

/// <summary>1 HP additions: network strip, Rift / reset schedule, tray links (English defaults; ru in UiText.cs, uk in UiText.Uk.cs).</summary>
public sealed partial class UiText
{
    public string NetNoGame { get; init; } = "no game connection";
    public string NetPing { get; init; } = "ping {0} ms";
    public string NetPingNoReply { get; init; } = "server ignores ping";
    public string NetLoss { get; init; } = "loss {0}%";
    public string NetRouterLoss { get; init; } = "to router {0}%";
    public string NetTipServer { get; init; } = "Game server {0}: {1}";
    public string NetTipPing { get; init; } = "ping {0} ms (min {1}, max {2}, jitter {3} ms), lost pings {4}%";
    public string NetTipNoPing { get; init; } = "does not answer ping — loss is read from the game stream";
    public string NetTipStream { get; init; } = "Game stream, last minute: {0} packets, {1} lost and re-sent ({2}%)";
    public string NetTipStreamWait { get; init; } = "Game stream: not enough traffic yet";
    public string NetTipRouter { get; init; } = "Router {0}: {1}";
    public string NetAdviceHome { get; init; } = "Packets are lost before the router: Wi-Fi or cable. Try a cable or 5 GHz.";
    public string NetAdviceRoute { get; init; } = "Loss beyond your home: run the network check (tray → Network check) to see where.";
    public string RiftIn { get; init; } = "Rift in {0}";
    public string RiftOpenFor { get; init; } = "Rift open {0}";
    public string ResetIn { get; init; } = "reset in {0}";
    public string ScheduleTip { get; init; } = "Spacetime Rift every 3 h from 00:00 UTC, open 10 min (next at {0}). Daily reset at {1}, weekly {2}.";
    public string TraySite { get; init; } = "1 HP guide (site)";
    public string TrayNetCheck { get; init; } = "Network check…";

    /// <summary>"2:05" for an hour and more, "17 min" below.</summary>
    public string Countdown(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:00}" : $"{Math.Max(1, (int)Math.Ceiling(d.TotalMinutes))} {MinutesShort}";
}
