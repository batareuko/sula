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
    public string PartyStateOff { get; init; } = "Now: everyone around is listed (EVERYONE).";
    public string PartyStateSelfUnknown { get; init; } = "Now: not filtering yet — the meter does not know your character. Change zone or teleport once.";
    public string PartyStateInstance { get; init; } = "Now: in an instance, where everyone is your group.";
    public string PartyStateSolo { get; init; } = "Now: on your own — only you are listed.";
    public string PartyStateParty { get; init; } = "Now: your party — {0}.";
    public string PartyOnlyChip { get; init; } = "PARTY";
    public string EveryoneChip { get; init; } = "EVERYONE";
    public string OneHpCompact { get; init; } = "Compact overlay (smaller header and rows)";
    public string OneHpDynamic { get; init; } = "Dynamic overlay: one line out of combat, a row per fighter in a fight";
    public string OneHpDynamicHint { get; init; } =
        "Out of combat the card is one line: your name, combat power and ping. When you or your party hit something it opens with a row for each of you and folds back 15 s after the fight.";
    public string MiniWaiting { get; init; } = "Waiting for the game…";
    public string DummyRun { get; init; } = "Run {0}";
    public string SecEvents { get; init; } = "EVENT ALERTS";
    public string RunTitle { get; init; } = "🏁 {0}";
    public string RunDetail { get; init; } = "{0} · bosses {1}/{2} · deaths {3}";
    public string RunCleared { get; init; } = "cleared";
    public string RunRowTip { get; init; } = "{0} over the run\ndamage {1} · {2}/s in fights\nhealing {3}\ndeaths {4}";
    public string PvpTitle { get; init; } = "⚔ PvP · {0} kills · {1} deaths";
    public string PvpDetail { get; init; } = "dealt {0} · taken {1} · burst {2}";
    public string PvpDealt { get; init; } = "DEALT";
    public string PvpTaken { get; init; } = "TAKEN";
    public string PvpRowTip { get; init; } = "{0}\nyou dealt {1} ({2} hits)\nthey dealt you {3} ({4} hits)";
    public string PvpDefeated { get; init; } = "☠ defeated";
    public string PvpKilledYou { get; init; } = "⚔ killed you";
    public string PvpOverlayOn { get; init; } = "PvP card: your opponents while you fight players (Abyss)";
    public string SourceShared { get; init; } = "From a guild member's in-game boss list ({0}) — shared 1 HP timers";
    public string EventsHint { get; init; } = "A notice with a sound over the game (and in the tray) before the event starts. Times: Global servers.";
    public string EventLead { get; init; } = "Notify this many minutes before";
    public string EventSoon { get; init; } = "{0} in {1} min ({2})";
    public string EventNow { get; init; } = "{0} starts now ({1})";
    public string EventsTitle { get; init; } = "1 HP · events";
    public string EventName(string id) => (Code, id) switch
    {
        ("uk", "rift") => "Розлом", ("uk", "shugo") => "Фестиваль Шуго", ("uk", "kaira") => "Наглядач Кайра",
        ("uk", "siege") => "Облога артефактів", ("uk", "siege-bosses") => "Боси облоги", ("uk", "nahma") => "Лорд-охоронець Нахма",
        ("uk", "daily-reset") => "Щоденний ресет", ("uk", "weekly-reset") => "Тижневий ресет",
        ("ru", "rift") => "Разлом", ("ru", "shugo") => "Фестиваль Шуго", ("ru", "kaira") => "Наблюдатель Кайра",
        ("ru", "siege") => "Осада артефактов", ("ru", "siege-bosses") => "Боссы осады", ("ru", "nahma") => "Лорд-страж Нахма",
        ("ru", "daily-reset") => "Ежедневный сброс", ("ru", "weekly-reset") => "Еженедельный сброс",
        (_, "rift") => "Spacetime Rift", (_, "shugo") => "Shugo Festival", (_, "kaira") => "Watcher Kaira",
        (_, "siege") => "Artifact Siege", (_, "siege-bosses") => "Siege bosses", (_, "nahma") => "Guardian Lord Nahma",
        (_, "daily-reset") => "Daily reset", (_, "weekly-reset") => "Weekly reset",
        _ => id,
    };
    public string DeathTitle { get; init; } = "☠ {0}";
    public string GearScoreShort { get; init; } = "GS {0}";
    public string GearScoreInspect { get; init; } = "Inspect the player in the game to see their gear score here.";
    public string DeathDetail { get; init; } = "killed you in {0} s · {1} damage taken";
    public string DeathBadge { get; init; } = "death recap";
    public string DeathTip { get; init; } =
        "Who hit you in the 15 s before you died, with the damage they dealt you (pets and skill effects with their owner). Hover a row for the skills. Gear score (GS) shows for players you inspected in the game.";
    public string TimerStale { get; init; } = "since {0} · open the map list in game";
    public string TimerStaleTip { get; init; } =
        "This time is old: the boss may have been killed since. Open the world map in the game (the field boss list) — the overlay reads the exact times from it.";
    public string OpenBossListHint { get; init; } =
        "Boss times need refreshing: in the game open the map → Exploration → Field Monsters for a few seconds. The overlay reads the exact respawn times of your server from it.";
    public string OpenBossListShort { get; init; } = "📍 Open the map → Field Monsters: boss times";
    public string TipBossMap { get; init; } = "On the online map (interactivemap.app): the boss's zone, turn on “Named Bosses”";
    public string DummyVsBest { get; init; } = "best {0}";
    public string DummyVsLast { get; init; } = "last {0}";
    public string DummyTip { get; init; } =
        "Training dummy, run {0}: your DPS {1}.\nAgainst your best run on this dummy: {2}; against the run before: {3}.\nStop hitting for 5 s and the next hit starts the next run (runs under 10 s do not count).";
    public string StreamOn { get; init; } = "Stream page for OBS (browser source)";
    public string StreamHint { get; init; } =
        "In OBS add a Browser source with the address below (width 480, height 600). It shows the meter during fights and nothing out of combat; add ?idle=1 to keep your name, combat power and ping on screen.";
    public string MiniPowerTip { get; init; } = "Combat power (from the party list in the game, or your profile on the 1 HP site)";
    public string PowerTip { get; init; } = "Combat power {0}";

    /// <summary>"2:05" for an hour and more, "17 min" below.</summary>
    public string Countdown(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:00}" : $"{Math.Max(1, (int)Math.Ceiling(d.TotalMinutes))} {MinutesShort}";
}
