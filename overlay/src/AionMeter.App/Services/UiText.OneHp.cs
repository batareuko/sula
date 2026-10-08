namespace AionMeter.App.Services;

/// <summary>1 HP additions: network strip, Rift / reset schedule, tray links (English defaults; ru in UiText.cs, uk in UiText.Uk.cs).</summary>
public sealed partial class UiText
{
    public string NetNoGame { get; init; } = "no game connection";
    public string NetGameNotRunning { get; init; } = "game not found";
    public string NetGameNoConnection { get; init; } = "game not connected yet";
    public string NetViaAccelerator { get; init; } = "via ping accelerator";
    public string NetTipAccelerator { get; init; } = "The game goes through a local ping accelerator (ExitLag, LagoFast …): only it sees the ping to the server. Loss is read from the game stream.";
    public string NetPing { get; init; } = "ping {0} ms";
    public string NetPingNoReply { get; init; } = "server ignores ping";
    public string NetLoss { get; init; } = "loss {0}%";
    public string NetLossBoth { get; init; } = "loss ↓{0}% ↑{1}%";
    public string NetStalls { get; init; } = "freezes {0}";
    public string NetTipUpstream { get; init; } = "Your packets to the server, last minute: {0}, sent again {1} ({2}%): skills that reach the server late";
    public string NetTipStalls { get; init; } = "Server stream froze {0} times for 0.5 s or more (together {1} s) in the last minute";
    public string NetTipNoStalls { get; init; } = "No freezes of the server stream in the last minute";
    public string NetAdviceStalls { get; init; } = "Freezes without packet loss: a delay spike on the route or the server itself is busy. Run the network check during the lag; the ping test shows delay spikes per hop.";
    public string NetAdviceUpstream { get; init; } = "Your packets get lost on the way to the server: usually Wi-Fi or an upload-saturated line (cloud sync, streaming). Try a cable and pause uploads.";
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

    public string RecordBest { get; init; } = "best {0}";
    public string RecordTip { get; init; } = "Your best DPS on this boss: {0}/s ({1}). This fight: {2}/s, {3}.";
    public string RecordNew { get; init; } = "new record!";
    public string GuildBadge { get; init; } = "1 HP {0}/{1}";
    public string GuildTip { get; init; } = "Your place among 1 HP members on this boss (each member's best kill): {0} of {1}.";
    public string GuildTop { get; init; } = " Top {0}%.";
    public string GuildPersonalBest { get; init; } = " New personal best on this boss!";
    public string GuildError { get; init; } = "Not sent to the 1 HP rating: {0}";
    public string GearTip { get; init; } = "Item level {0} · combat power {1}";
    public string SecOneHp { get; init; } = "1 HP";
    public string OneHpKeyLabel { get; init; } = "Overlay key";
    public string OneHpKeyHint { get; init; } = "Create it on guide.sulaslova.com → DPS rating and paste it here. Only your own boss kills are sent: character, class, DPS, damage, fight length and your place in the party.";
    public string OneHpUpload { get; init; } = "Send my boss kills to the 1 HP DPS rating";
    public string OneHpGear { get; init; } = "Show players' item level (looked up on the 1 HP site by name)";
    public string OneHpCheck { get; init; } = "Check key";
    public string OneHpKeyOk { get; init; } = "The key works.";
    public string OneHpKeyBad { get; init; } = "The site does not know this key: create a new one.";
    public string OneHpKeyOffline { get; init; } = "Could not reach the site.";
    public string OneHpWithGame { get; init; } = "Start with AION 2 and close after it";
    public string OneHpWithGameHint { get; init; } = "A tiny helper starts with Windows and opens the overlay when the game starts; the overlay closes about 15 seconds after the game.";
    public string UpdateZipHint { get; init; } = "The overlay closes, its files are replaced with the new version and it starts again. Settings, fight history and timers stay.";
    public string OneHpBossSync { get; init; } = "Send field boss times from the game to the 1 HP site (your server only)";
    public string OneHpPriorityBosses { get; init; } = "Notify about priority field bosses (★) 10 minutes ahead, without a bell";
    public string PriorityLoot { get; init; } = "★ Priority: level 48–51 field boss with its own Unique set, about one piece per kill.";
    public string OneHpPartyOnly { get; init; } = "Only me and my party (players around who are not in it are hidden)";
    public string TipPartyOnly { get; init; } = "Outside dungeons only you and your party are listed: players nearby who hit the same monster are left out of the rows, the totals and the places. In dungeons and raids everyone is your group anyway.";
    public string PartyOnlyChip { get; init; } = "PARTY";
    public string EveryoneChip { get; init; } = "EVERYONE";
    public string OneHpCompact { get; init; } = "Compact overlay (smaller header and rows)";

    /// <summary>"2:05" for an hour and more, "17 min" below.</summary>
    public string Countdown(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:00}" : $"{Math.Max(1, (int)Math.Ceiling(d.TotalMinutes))} {MinutesShort}";
}
