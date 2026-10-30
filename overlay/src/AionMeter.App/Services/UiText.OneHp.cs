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
    public string SendLogTray { get; init; } = "📄 Send the log (disconnects, errors)";
    public string LogSent { get; init; } = "Log sent — the link is in the clipboard: paste it (Ctrl+V) in the chat.";
    public string AnalysisTrayStart { get; init; } = "● Record for analysis";
    public string AnalysisTrayStop { get; init; } = "■ Stop recording and send";
    public string AnalysisStartTip { get; init; } = "Record for analysis: packets and events of the fight, sent with one more click";
    public string AnalysisStopTip { get; init; } = "Recording {0}:{1:00} — click to stop and send";
    public string AnalysisStarted { get; init; } = "Recording for analysis. Play (fight, change party, teleport), then press ● again to send. Stops by itself after 20 min.";
    public string AnalysisPacking { get; init; } = "Packing the recording…";
    public string AnalysisSending { get; init; } = "Sending the recording";
    public string AnalysisSent { get; init; } = "Recording sent — the link is in the clipboard: paste it (Ctrl+V) in the chat.";
    public string AnalysisNoKey { get; init; } = "No 1 HP key (Settings → 1 HP): the zip is on the Desktop — drag it into the chat.";
    public string AnalysisFailed { get; init; } = "Not sent ({0}): the zip is on the Desktop — drag it into the chat.";

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
    public string TipPartyOnly { get; init; } = "Outside dungeons only you and your party are listed: players nearby who hit the same monster are left out of the rows, the totals and the places. In dungeons and raids everyone is your group anyway. A party member missing (invited mid-fight)? Switch to EVERYONE, right-click them → Add to my party.";
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
        ("uk", "invasion") => "Вторгнення", ("uk", "arena") => "Арена стратегії",
        ("uk", "siege") => "Облога артефактів", ("uk", "siege-bosses") => "Боси облоги", ("uk", "nahma") => "Лорд-охоронець Нахма",
        ("uk", "daily-reset") => "Щоденний ресет", ("uk", "weekly-reset") => "Тижневий ресет",
        ("ru", "rift") => "Разлом", ("ru", "shugo") => "Фестиваль Шуго", ("ru", "kaira") => "Наблюдатель Кайра",
        ("ru", "invasion") => "Вторжение", ("ru", "arena") => "Арена стратегии",
        ("ru", "siege") => "Осада артефактов", ("ru", "siege-bosses") => "Боссы осады", ("ru", "nahma") => "Лорд-страж Нахма",
        ("ru", "daily-reset") => "Ежедневный сброс", ("ru", "weekly-reset") => "Еженедельный сброс",
        (_, "rift") => "Spacetime Rift", (_, "shugo") => "Shugo Festival", (_, "kaira") => "Watcher Kaira",
        (_, "invasion") => "Invasion", (_, "arena") => "Arena of Strategy",
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
    public string PartySourceName(Core.Combat.PartySource s) => (Code, s) switch
    {
        ("uk", Core.Combat.PartySource.Game) => "Джерело: список групи з гри",
        ("uk", Core.Combat.PartySource.Roster) => "Джерело: список групи (кімната)",
        ("uk", Core.Combat.PartySource.Loot) => "Джерело: здобич після вбивств (поки гра не надіслала список групи)",
        ("uk", Core.Combat.PartySource.Heals) => "Джерело: кого ви лікуєте (поки гра не надіслала список групи)",
        ("uk", Core.Combat.PartySource.Manual) => "Джерело: додані вручну",
        ("ru", Core.Combat.PartySource.Game) => "Источник: список группы из игры",
        ("ru", Core.Combat.PartySource.Roster) => "Источник: список группы (комната)",
        ("ru", Core.Combat.PartySource.Loot) => "Источник: добыча после убийств (пока игра не прислала список группы)",
        ("ru", Core.Combat.PartySource.Heals) => "Источник: кого вы лечите (пока игра не прислала список группы)",
        ("ru", Core.Combat.PartySource.Manual) => "Источник: добавлены вручную",
        (_, Core.Combat.PartySource.Game) => "Source: the game's party list",
        (_, Core.Combat.PartySource.Roster) => "Source: the party roster (room)",
        (_, Core.Combat.PartySource.Loot) => "Source: loot shares after kills (until the game sends the party list)",
        (_, Core.Combat.PartySource.Heals) => "Source: players you heal (until the game sends the party list)",
        (_, Core.Combat.PartySource.Manual) => "Source: added by hand",
        _ => "",
    };
    public string ThreatDanger { get; init; } = "Run";
    public string ThreatCaution { get; init; } = "Stronger";
    public string ThreatWeak { get; init; } = "Weaker";
    public string ThreatEven { get; init; } = "Even";
    public string ThreatYou { get; init; } = "you";
    public string ThreatRun { get; init; } = "Run! {0} ({1}) is far stronger: {2}";
    public string ThreatRunVoice { get; init; } = "Run, {0} is much stronger";
    public string PvpThreatOn { get; init; } = "PvP colours: red — run, orange — stronger, green — weaker; a notice when a red one hits you";
    public string PvpThreatHint { get; init; } =
        "Gaps (theirs minus yours) for red / orange / green: gear score when both are inspected, else combat power (sent for everyone in view).";
    public string PvpGearGaps { get; init; } = "Gear score";
    public string PvpPowerGaps { get; init; } = "Combat power";
    public string BuffsOn { get; init; } = "Your lasting buffs on the card (10 s and more)";
    public string FramelessOn { get; init; } = "No frame: a dark header and the rows straight over the game";
    public string AnimatedNamesOn { get; init; } = "Shimmering names in the class colours";
    public string DiagTitle { get; init; } = "Disconnect test (switch one thing off, restart the meter, play 20 min)";
    public string DiagNoOverlayOn { get; init; } = "No window over the game (the card and the boss bar stay hidden)";
    public string DiagNoCaptureOn { get; init; } = "No packet capture (Npcap is not opened; the meter counts nothing)";
    public string DiagNoGameScanOn { get; init; } = "Do not look for the game and do not ping (process list, connections)";
    public string DiagSilentOn { get; init; } = "Complete silence: only the tray icon (no hotkeys, internet, start with the game either)";
    public string DiagCaptureOff { get; init; } = "Packet capture is off (disconnect test in Settings)";
    public string TabAnalysis { get; init; } = "Skill analysis";
    public string ThUses { get; init; } = "Uses";
    public string ThPerUse { get; init; } = "Per use";
    public string ThBestUse { get; init; } = "Best use";
    public string ThPerMinute { get; init; } = "Per min";
    public string ThWhen { get; init; } = "When in the fight";
    public string AnActiveDps { get; init; } = "ACTIVE DPS";
    public string AnActiveTime { get; init; } = "dealing damage {0} ({1} of the fight)";
    public string AnPeak5 { get; init; } = "PEAK 5 s";
    public string AnPeak10 { get; init; } = "PEAK 10 s";
    public string AnPeakAt { get; init; } = "from {0}";
    public string AnSkills { get; init; } = "SKILLS";
    public string AnUsesTotal { get; init; } = "~{0} uses";
    public string AnalysisNote { get; init; } = "Uses are estimated: the game sends hits, not casts, so hits of one skill less than 1.2 s apart count as one use. Damage over time is in the skill's damage but not in its uses.";
    public string MapTitle { get; init; } = "World map";
    public string MapTray { get; init; } = "🗺 World map (Ctrl+Shift+M)";
    public string MapCredit { get; init; } = "Maps and places: Aion2Data";
    public string MapHint { get; init; } = "{0} · {1} m across · wheel — zoom, drag — move, point at a dot — its name";
    public string MapTip { get; init; } = "Wheel — zoom, drag — move. Pets you have (ticked on the card) are grey.";
    public string MapLoading { get; init; } = "Downloading the map picture…";
    public string MapNoPicture { get; init; } = "The map picture could not be downloaded (internet?). The places are shown without it.";
    public string MapNoCodec { get; init; } = "Windows cannot open the map picture (WebP): install \"WebP Image Extensions\" from the Microsoft Store (free). The places are shown without it.";
    public string MapNoData { get; init; } = "No map table (data/world_maps.json).";
    public string LayerBosses { get; init; } = "Field bosses";
    public string LayerDungeons { get; init; } = "Dungeons";
    public string LayerRifts { get; init; } = "Rifts";
    public string LayerKibelisks { get; init; } = "Kibelisks";
    public string LayerMonoliths { get; init; } = "Monoliths";
    public string LayerGather { get; init; } = "Gathering";
    public string LayerPets { get; init; } = "Pet souls";
    public string LayerCubes { get; init; } = "Hidden Cube spots";

    public string MapLayer(string key) => key switch
    {
        "bosses" => LayerBosses,
        "dungeons" => LayerDungeons,
        "rifts" => LayerRifts,
        "kibelisks" => LayerKibelisks,
        "monoliths" => LayerMonoliths,
        "gather" => LayerGather,
        "pets" => LayerPets,
        "cubes" => LayerCubes,
        _ => key,
    };

    public string UiScale { get; init; } = "Interface scale";
    public string UiScaleHint { get; init; } = "The overlay, the boss bar and the pet radar; over the rows: Ctrl + Shift + wheel";
    public string ClickToGame { get; init; } = "Click to go back to the game";
    public string HideBehindOn { get; init; } = "Hide the meter while another program is in front of the game (browser, Discord, desktop)";
    public string MechanicsOn { get; init; } = "Boss mechanics in boss fights: the boss's casts, what comes next (learnt from your fights) and the guide's tips";
    public string MechNext { get; init; } = "Next: {0}";
    public string MechNow { get; init; } = "now";
    public string MechTips { get; init; } = "Mechanics · {0}";
    public string MechLearning { get; init; } = "Learning the boss's rhythm: after a fight or two the card says what comes next";
    public string NetPingOn { get; init; } = "Ping the game server and the router (for the network strip); the log notes every drop of the game connection either way";
    public string TwoLineRowsOn { get; init; } = "Two-line rows: deaths and trend by the name, crit, gear and server below; DPS / time / total tiles";
    public string ThemeLabel { get; init; } = "Card colours";
    public string[] ThemeNames { get; init; } = ["Slate", "Crimson", "Emerald", "Violet", "Amber"];
    public string TabTaken { get; init; } = "Taken";
    public string TileHps { get; init; } = "HPS";
    public string TileTakenPs { get; init; } = "Taken/s";
    public string TileDuration { get; init; } = "Duration";
    public string TileTotal { get; init; } = "Total";
    public string TileAps { get; init; } = "APS";
    public string TileHits { get; init; } = "Hits";
    public string TabApsTip { get; init; } = "APS — hits per second: how often each player lands a hit";
    public string DeathsTip { get; init; } = "Deaths in this fight";
    public string TrendTip { get; init; } = "DPS over the last 5 seconds";
    public string PetRadarOn { get; init; } = "Pet radar: monsters in view that drop a pet's soul (data: Aion2Data)";
    public string PetRadarReset { get; init; } = "Clear the \"have\" marks ({0})";
    public string PetRadarTitle { get; init; } = "SOULS NEARBY";
    public string PetRadarTip { get; init; } = "{0} ({1}) — soul from {2}\nIn view: {3}\nWhere: {4}\n\nClick: I have it / I do not";
    public string PetRadarTray { get; init; } = "🐾 Pet radar (Ctrl+Shift+P)";
    public string PetRadarNoCenter { get; init; } = "hit a monster: the radar centres on you";
    public string PetRadarWindowTip { get; init; } = "Soul monsters around you: bright — pets you do not have yet, grey — you have them.\nThe centre is the monster you hit last.\nWheel — range, right click — turn a quarter (line it up with the map), drag — move.";
    public string PetRadarCount { get; init; } = "missing {0} · have {1}";
    public string CubeRadarOn { get; init; } = "Hidden Cube radar: \"Cube detected\" while a hidden cube is near";
    public string PetHave { get; init; } = "✓ have";
    public string PetMissing { get; init; } = "missing";
    public string PetHideOwnedOn { get; init; } = "Pet radar: leave out the pets I already have";
    public string BuffEndOn { get; init; } = "Notice 10 s before a buff of a minute or more runs out";
    public string BuffEnding { get; init; } = "{0} ends in {1} s";
    public string CooldownsOn { get; init; } = "Your party's cooldowns of a minute and more on the card";
    public string PartyRequestOn { get; init; } = "Notice when someone asks to join your party (class, level, combat power)";
    public string PartyRequest { get; init; } = "Asks to join: {0}";
    public string LevelShort { get; init; } = "lvl {0}";
    public string ResourcesOn { get; init; } = "Your aether, Shugo keys and tickets on the card (out of combat; tooltip: all characters)";

    public string ResourceName(int id) => (Code, id) switch
    {
        ("uk", Core.Events.ResourceEvent.Aether) => "Етер", ("uk", Core.Events.ResourceEvent.ShugoKeys) => "Ключі Шуго",
        ("uk", Core.Events.ResourceEvent.Nightmare) => "Кошмар", ("uk", Core.Events.ResourceEvent.Invasion) => "Вторгнення",
        ("uk", Core.Events.ResourceEvent.Awakening) => "Пробудження",
        ("ru", Core.Events.ResourceEvent.Aether) => "Эфир", ("ru", Core.Events.ResourceEvent.ShugoKeys) => "Ключи Шуго",
        ("ru", Core.Events.ResourceEvent.Nightmare) => "Кошмар", ("ru", Core.Events.ResourceEvent.Invasion) => "Вторжение",
        ("ru", Core.Events.ResourceEvent.Awakening) => "Пробуждение",
        (_, Core.Events.ResourceEvent.Aether) => "Aether", (_, Core.Events.ResourceEvent.ShugoKeys) => "Shugo keys",
        (_, Core.Events.ResourceEvent.Nightmare) => "Nightmare", (_, Core.Events.ResourceEvent.Invasion) => "Invasion",
        (_, Core.Events.ResourceEvent.Awakening) => "Awakening",
        _ => "#" + id,
    };
    public string DefHealingDone { get; init; } = "Healing done";
    public string DefHealingReceived { get; init; } = "Healing received";
    public string DefTakenFrom { get; init; } = "Damage taken from";
    public string DefHealedBy { get; init; } = "Healed by";
    public string BossBarOn { get; init; } = "Big boss HP bar at the top of the screen (boss fights)";
    public string PartyDeathOn { get; init; } = "Sound and notice when a party member dies";
    public string VoiceOn { get; init; } = "Speak the alerts (Windows voice)";
    public string PartyDied { get; init; } = "☠ {0} died";
    public string BusCount { get; init; } = "🚌 {0} down · {1} bosses this session";
    public string ForecastEnrage { get; init; } = "enrage {0}";
    public string ForecastMissing { get; init; } = "+{0} DPS";
    public string ForecastTip { get; init; } =
        "At the pace of the last 30 s ({0} DPS on the boss) it falls in ⏱. Enrage: counted from the pull; to kill before it, {1} DPS. Red: the boss enrages first.";
    public string StreamOn { get; init; } = "Stream page for OBS (browser source)";
    public string StreamHint { get; init; } =
        "In OBS add a Browser source with the address below (width 480, height 600). It shows the overlay exactly as it is on your screen, transparent around the card. A lighter card instead: the address + card (…/card, add ?idle=1 to keep your name, combat power and ping out of combat).";
    public string MiniPowerTip { get; init; } = "Combat power (from the party list in the game, or your profile on the 1 HP site)";
    public string PowerTip { get; init; } = "Combat power {0}";

    /// <summary>"2:05" for an hour and more, "17 min" below.</summary>
    /// <summary>1 HP: the event strip's countdown — 27:20 under an hour, then 1h 27m, then 1d 7h.</summary>
    public string ShortCountdown(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        var (h, m, dd) = Code switch { "uk" => ("г", "хв", "д"), "ru" => ("ч", "м", "д"), _ => ("h", "m", "d") };
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes}:{d.Seconds:00}";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours}{h} {d.Minutes}{m}";
        return $"{(int)d.TotalDays}{dd} {d.Hours}{h}";
    }

    public string Countdown(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours}:{d.Minutes:00}" : $"{Math.Max(1, (int)Math.Ceiling(d.TotalMinutes))} {MinutesShort}";
}
