using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AionMeter.App.Controls;
using AionMeter.App.Services;
using AionMeter.App.ViewModels;
using AionMeter.Core;
using AionMeter.Core.Capture;
using AionMeter.Core.Combat;
using AionMeter.Core.Storage;

namespace AionMeter.App.Windows;

public partial class OverlayWindow : Window
{
    private readonly MeterService _meter;
    private readonly AppSettings _settings;
    private readonly OverlayViewModel _vm = new();
    private Guid? _segment; // in-memory segment; null = live
    private SavedFight? _saved; // a fight loaded from history (takes precedence over _segment)
    private nint _hwnd;
    private readonly DragAnywhere _drag;

    private sealed record SavedFight(HistoryEntry Entry, FightRecord Record, RecordFightView View, string Title);

    /// <summary>The render tool's fight clock for animation frames (Unix ms); null = real time.</summary>
    internal static Func<long>? ClockOverride { get; set; }

    public OverlayWindow(MeterService meter)
    {
        _meter = meter;
        _settings = meter.Settings;
        var opacity = _settings.BackgroundOpacity; // the slider's coercion during InitializeComponent would overwrite it
        InitializeComponent();
        _settings.BackgroundOpacity = opacity;
        DataContext = _vm;

        Left = _settings.OverlayLeft;
        Top = _settings.OverlayTop;
        Width = Math.Max(MinWidth, _settings.OverlayWidth);
        if (!_settings.DynamicOverlay) Height = Math.Max(MinHeight, _settings.OverlayHeight); // dynamic: as tall as its content
        EnsureOnScreen();

        OpacitySlider.Value = _settings.BackgroundOpacity;
        FrameBackground.Opacity = _settings.BackgroundOpacity;
        ApplyLock();
        ApplyCompact();
        ApplyRowSize(_settings.RowSize);
        ApplyDynamic();
        UpdateModeLabel();
        ApplyHotkeyTips();
        _meter.Updates.Changed += ShowUpdateBanner;
        _meter.Tracker.DeathRecapped += r => _death = r; // 1 HP: shown on the card right after you die
        _death = _meter.Tracker.LastDeath;
        ShowUpdateBanner();

        // Drag the card by any part of it (unless locked); a click without movement still opens a breakdown.
        _drag = DragAnywhere.Attach(this, canDrag: () => !_settings.Locked, dropped: SavePlacement);

        // The card stays clean: its buttons only appear while the mouse is over it.
        MouseEnter += (_, _) => UpdateChrome();
        MouseLeave += (_, _) => UpdateChrome();

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            // Clicking the overlay must never pull keyboard focus away from the game window.
            NativeMethods.SetExStyle(_hwnd, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
            ApplyClickThrough();
            FitToMonitor();
        };
    }

    public nint Handle => _hwnd;

    /// <summary>Called by the app's UI timer (≈5 Hz).</summary>
    public void Refresh()
    {
        // Looking at an old fight when a new pull starts: jump back to live so the fight is never missed.
        if ((_saved is not null || _segment is not null) &&
            _meter.Tracker.Segments() is [{ IsActive: true } running, ..] && running.StartedAt > _selectedAt)
        {
            _saved = null;
            _segment = null;
        }

        EncounterSnapshot? shown;
        if (_saved is { } saved)
        {
            shown = saved.Record.Summary;
            _vm.Apply(saved.Record.Summary with { Title = saved.Title, Zone = _meter.DisplayZone(saved.Record.Summary.Zone) },
                _settings.MaxRows, _settings.BarsRelativeToTop);
            _vm.SetSegment("saved", saved.Entry.StartedAt.ToString("dd.MM HH:mm"));
            _vm.Portrait = _meter.PortraitOf(saved.Entry);
        }
        else
        {
            var now = ClockOverride?.Invoke() ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var snap = _meter.Tracker.Snapshot(_segment, now);
            if (snap is null && _segment is not null)
            {
                // The selected segment fell out of the in-memory list.
                _segment = null;
                snap = _meter.Tracker.Snapshot(null, now);
            }
            // 1 HP: you just died: who killed you, until your next fight starts
            // (for 20 s: a fight that goes on without you — a raid — comes back after that)
            if (_segment is null && _death is { } death && now - death.DeathMs < 20_000 &&
                (snap is null || snap.StartedAt.ToUnixTimeMilliseconds() < death.DeathMs))
                snap = RecapSnapshot(death);
            else _death = null;
            shown = snap;
            _vm.Apply(snap, _settings.MaxRows, _settings.BarsRelativeToTop);
            _vm.SetSegment(_segment is null ? "live" : "session", snap?.StartedAt.ToString("HH:mm"));
            _vm.Portrait = snap is null ? null : _meter.PortraitOf(snap);
        }
        SetExpanded(WantExpanded(shown));
        if (!_settings.ShowBossPanel || _expanded == false) _vm.HasBoss = false;
        if (_death is { } shownDeath && shown?.Id == shownDeath.Id) ApplyDeath(shownDeath);
        else ApplyOneHp(shown, _saved?.Entry.FileName);
        if (Environment.TickCount64 - _timersLabelAt >= 1_000) UpdateTimersLabel();
        if (Environment.TickCount64 - _oneHpAt >= 1_000) UpdateOneHpStrip();

        var status = _meter.DemoRunning ? new CaptureStatus(CaptureState.Capturing, "Demo fight running") : _meter.CaptureStatus;
        // Names are only sent on a loading screen: players already around when the meter started show as #id.
        var unnamed = _vm.Rows.Any(r => r.Name.Contains('#')); // "Templar #10388": the server has not sent this name yet
        _vm.Status = unnamed && status.State == CaptureState.Capturing
            ? "Names appear after the next loading screen"
            : status.Message;
        // Footer reads like the card's caption; capture problems take its place so they are never missed.
        FooterText.Text = status.State is CaptureState.Capturing && _vm.HasData
            ? (unnamed ? _vm.Footer + " · " + UiText.Current.NamesLater : _vm.Footer)
            : _vm.Status;
        _vm.StatusBrush = status.State switch
        {
            CaptureState.Capturing => (Brush)FindResource("Green"),
            CaptureState.Error => (Brush)FindResource("Red"),
            CaptureState.WaitingForGame or CaptureState.Starting => (Brush)FindResource("Amber"),
            _ => (Brush)FindResource("TextMute"),
        };
    }

    // ---------------------------------------------------------------- 1 HP: death recap
    private volatile DeathRecap? _death;

    /// <summary>The recap as a fight on the card: the attackers as rows, with the damage they dealt you.</summary>
    private EncounterSnapshot RecapSnapshot(DeathRecap r)
    {
        var t = UiText.Current;
        var seconds = Math.Max(1, r.DurationMs) / 1000.0;
        var total = Math.Max(1, r.TotalDamage);
        var rows = r.Attackers.Select(a => new CombatantSnapshot(a.ActorId, a.Name, a.Class, false, a.Damage, a.Damage / seconds,
            (double)a.Damage / total, a.Hits, a.Hits > 0 ? (double)a.Crits / a.Hits : 0, a.MaxHit, 0, a.ServerId)).ToList();
        var server = _meter.Data.ServerName(r.KillerServer);
        var title = string.Format(t.DeathTitle, r.KillerName) + (string.IsNullOrEmpty(server) ? "" : " · " + server);
        return new EncounterSnapshot(r.Id, title, null, DateTimeOffset.FromUnixTimeMilliseconds(r.FirstHitMs).ToLocalTime(),
            r.DurationMs, Math.Max(1_000, r.DurationMs), false, EncounterEndReason.Manual, r.TotalDamage, r.TotalDamage / seconds,
            null, rows);
    }

    private void ApplyDeath(DeathRecap r)
    {
        var t = UiText.Current;
        _vm.Detail = string.Format(t.DeathDetail, (r.DurationMs / 1000.0).ToString("0.0", t.Culture), r.TotalDamage.ToString("#,0", t.Culture));
        _vm.Record = t.DeathBadge;
        _vm.RecordTip = t.DeathTip;
        _vm.Guild = _vm.GuildTip = "";
        foreach (var row in _vm.Rows)
        {
            var a = r.Attackers.FirstOrDefault(x => x.ActorId == row.ActorId);
            if (a is null) continue;
            row.Gear = a.IsPlayer ? _meter.Data.ServerName(a.ServerId) : "";
            row.Tooltip = $"{a.Name} · {a.Damage:#,0} · {a.Hits}×, max {a.MaxHit:#,0}\n" +
                          string.Join("\n", a.Skills.Take(8).Select(s => $"{s.Name}: {s.Damage:#,0} ({s.Hits}×)"));
        }
    }

    // ---------------------------------------------------------------- 1 HP: party gear, personal record, guild place
    private long _recordAt;
    private Guid? _recordFor;

    private void ApplyOneHp(EncounterSnapshot? snap, string? savedFile)
    {
        var t = UiText.Current;
        if (snap is null)
        {
            _vm.Record = _vm.Guild = "";
            return;
        }
        foreach (var row in _vm.Rows)
        {
            var cb = snap.Combatants.FirstOrDefault(c => c.ActorId == row.ActorId);
            var gear = cb is null || cb.IsUnknownSummons ? null : _meter.Cloud.GearFor(cb.Name, cb.ServerId);
            // Combat power next to the name: the game's party list first, the site's gear lookup otherwise
            var power = cb?.Power is > 0 ? cb.Power : gear?.CombatPower ?? 0;
            row.Gear = power > 0 ? Format.Power(power) : "";
            // Apply() rebuilds the tooltip on every refresh, so the gear line is added once per refresh
            if (gear is not null)
                row.Tooltip += "\n" + string.Format(t.GearTip, gear.ItemLevel, power.ToString("#,0", t.Culture));
            else if (power > 0)
                row.Tooltip += "\n" + string.Format(t.PowerTip, power.ToString("#,0", t.Culture));
        }

        // 1 HP: a training dummy run: its number, against your best and your last run on that dummy
        if (savedFile is null && _meter.Dummies.Compare(snap, _meter.IsDummy) is { } run)
        {
            string Pct(double? v) => v is { } p ? (p >= 0 ? "+" : "−") + Math.Abs(p).ToString("0.0", t.Culture) + "%" : "—";
            _vm.Record = run.IsNewBest
                ? string.Format(t.DummyRun, run.Run) + " · " + t.RecordNew
                : string.Format(t.DummyRun, run.Run) + (run.VsBest is null ? "" : " · " + string.Format(t.DummyVsBest, Pct(run.VsBest)))
                  + (run.VsLast is null ? "" : " · " + string.Format(t.DummyVsLast, Pct(run.VsLast)));
            _vm.RecordTip = string.Format(t.DummyTip, run.Run, Format.Compact(run.Dps), Pct(run.VsBest), Pct(run.VsLast));
            _recordFor = null;
        }
        // The history list is read from disk: once a second is plenty.
        else if (_recordFor != snap.Id || Environment.TickCount64 - _recordAt >= 1_000)
        {
            _recordAt = Environment.TickCount64;
            _recordFor = snap.Id;
            var cmp = Core.OneHp.PersonalRecords.Compare(_meter.History.List(), snap, savedFile);
            if (cmp is null) _vm.Record = _vm.RecordTip = "";
            else
            {
                var delta = (cmp.DeltaPct >= 0 ? "+" : "") + cmp.DeltaPct.ToString("0.#", t.Culture) + "%";
                _vm.Record = cmp.IsNewRecord ? t.RecordNew : string.Format(t.RecordBest, Format.Compact(cmp.Best.SelfDps)) + " · " + delta;
                _vm.RecordTip = string.Format(t.RecordTip, Format.Compact(cmp.Best.SelfDps), cmp.Best.StartedAt.ToLocalTime().ToString("dd.MM HH:mm", t.Culture),
                    Format.Compact(cmp.CurrentDps), delta);
            }
        }

        var guild = _meter.Cloud.ResultFor(snap.Id);
        if (guild is null) _vm.Guild = _vm.GuildTip = "";
        else if (guild.Error is { } err)
        {
            _vm.Guild = "1 HP ⚠";
            _vm.GuildTip = string.Format(t.GuildError, err);
        }
        else
        {
            _vm.Guild = string.Format(t.GuildBadge, guild.Place, guild.Total);
            _vm.GuildTip = string.Format(t.GuildTip, guild.Place, guild.Total) +
                           (guild.TopPct is { } top ? string.Format(t.GuildTop, top) : "") + (guild.PersonalBest ? t.GuildPersonalBest : "");
        }
    }

    // ---------------------------------------------------------------- 1 HP strip: network health, Rift, reset
    private long _oneHpAt;

    private void UpdateOneHpStrip()
    {
        _oneHpAt = Environment.TickCount64;
        var t = UiText.Current;
        var c = t.Culture;
        var r = _meter.Net.Report();
        string Pct(double v) => v.ToString("0.#", c);

        // 1 HP: the PARTY chip says who counts as your party, or why nothing is filtered
        var party = _meter.Tracker.PartyStatus();
        _vm.PartyTip = t.TipPartyOnly + "\n\n" + party.State switch
        {
            Core.Combat.PartyFilterState.Off => t.PartyStateOff,
            Core.Combat.PartyFilterState.SelfUnknown => t.PartyStateSelfUnknown,
            Core.Combat.PartyFilterState.Instance => t.PartyStateInstance,
            Core.Combat.PartyFilterState.Solo => t.PartyStateSolo,
            _ => string.Format(t.PartyStateParty, string.Join(", ", party.Party)),
        };

        var parts = new List<string>();
        if (r.Server is null)
            parts.Add(r.Link switch
            {
                Services.GameLink.NotRunning => t.NetGameNotRunning,
                Services.GameLink.Accelerator => t.NetViaAccelerator,
                Services.GameLink.NoConnection => t.NetGameNoConnection,
                _ => t.NetNoGame,
            });
        else parts.Add(r.ServerPing.AnyReply && r.ServerPing.AvgMs is { } avg ? string.Format(t.NetPing, Math.Round(avg)) : t.NetPingNoReply);
        // Loss of the game stream is known whatever the route (also through an accelerator): from the server ↓ and to it ↑
        if (r.StreamLossPct is { } loss)
            parts.Add(r.UpResendPct is { } upPct ? string.Format(t.NetLossBoth, Pct(loss), Pct(upPct)) : string.Format(t.NetLoss, Pct(loss)));
        else if (r.Server is not null && r.ServerPing.AnyReply && r.ServerPing.Samples >= 10) parts.Add(string.Format(t.NetLoss, Pct(r.ServerPing.LossPct)));
        if (r.Stalls > 0) parts.Add(string.Format(t.NetStalls, r.Stalls));
        var routerBad = r.GatewayPing.Samples >= 10 && r.GatewayPing.AnyReply && r.GatewayPing.LossPct >= 1;
        if (routerBad) parts.Add(string.Format(t.NetRouterLoss, Pct(r.GatewayPing.LossPct)));
        NetText.Text = string.Join(" · ", parts);
        NetDot.Fill = (Brush)FindResource(r.Quality switch
        {
            Core.OneHp.NetQuality.Good => "Green",
            Core.OneHp.NetQuality.Fair => "Amber",
            Core.OneHp.NetQuality.Bad => "Red",
            _ => "TextMute",
        });
        MiniNetDot.Fill = NetDot.Fill;
        // The one-line card: ping, packet loss (from / to the server) and freezes
        MiniNetText.Text = string.Join(" · ", parts.Take(3));
        UpdateMini();

        string PingLine(Core.OneHp.NetSummary s) => s.AnyReply
            ? string.Format(t.NetTipPing, Math.Round(s.AvgMs ?? 0), s.MinMs, s.MaxMs, s.JitterMs?.ToString("0", c) ?? "—", Pct(s.LossPct))
            : t.NetTipNoPing;
        var tip = new List<string>();
        if (r.Server is not null) tip.Add(string.Format(t.NetTipServer, r.Server, PingLine(r.ServerPing)));
        else if (r.Link == Services.GameLink.Accelerator) tip.Add(t.NetTipAccelerator);
        tip.Add(r.StreamLossPct is { } sl ? string.Format(t.NetTipStream, r.StreamSegments.ToString("#,0", c), r.StreamHoles, Pct(sl)) : t.NetTipStreamWait);
        if (r.UpResendPct is { } up) tip.Add(string.Format(t.NetTipUpstream, r.UpSegments.ToString("#,0", c), r.UpResends, Pct(up)));
        tip.Add(r.Stalls > 0 ? string.Format(t.NetTipStalls, r.Stalls, (r.StallMs / 1000.0).ToString("0.#", c)) : t.NetTipNoStalls);
        if (r.Stalls > 0 && (r.StreamLossPct ?? 0) < 0.5 && (r.UpResendPct ?? 0) < 0.5) tip.Add(t.NetAdviceStalls);
        else if ((r.UpResendPct ?? 0) >= 0.5 && (r.StreamLossPct ?? 0) < 0.5) tip.Add(t.NetAdviceUpstream);
        if (r.Gateway is not null) tip.Add(string.Format(t.NetTipRouter, r.Gateway, PingLine(r.GatewayPing)));
        if (routerBad) tip.Add(t.NetAdviceHome);
        else if (r.Quality == Core.OneHp.NetQuality.Bad) tip.Add(t.NetAdviceRoute);
        NetPanel.ToolTip = MiniNet.ToolTip = NetText.Text + "\n\n" + string.Join("\n", tip);

        var now = DateTimeOffset.UtcNow;
        var rift = Core.OneHp.GlobalSchedule.Rift(now);
        var reset = Core.OneHp.GlobalSchedule.NextDailyReset(now);
        var weekly = Core.OneHp.GlobalSchedule.NextWeeklyReset(now);
        ScheduleText.Text = (rift.IsOpen ? string.Format(t.RiftOpenFor, t.Countdown(rift.Closes - now)) : string.Format(t.RiftIn, t.Countdown(rift.Opens - now)))
            + " · " + string.Format(t.ResetIn, t.Countdown(reset - now));
        ScheduleText.Foreground = rift.IsOpen ? (Brush)FindResource("Green") : new SolidColorBrush(Color.FromRgb(0xB4, 0xBC, 0xCB));
        RemindText.Text = ScheduleText.Text;
        RemindText.Foreground = ScheduleText.Foreground;
        string Local(DateTimeOffset d) => d.ToLocalTime().ToString("HH:mm", c);
        ScheduleText.ToolTip = RemindText.ToolTip = string.Format(t.ScheduleTip, Local(rift.IsOpen ? rift.Opens + Core.OneHp.GlobalSchedule.RiftEvery : rift.Opens), Local(reset),
            weekly.ToLocalTime().ToString("dddd HH:mm", c));
        // 1 HP: boss times older than 2 h — ask, on the card itself, to open the in-game boss list
        if (_meter.Tracker.SelfName is not null && r.Server is not null &&
            _meter.Timers.ListStale(_meter.Tracker.SelfServerId, AionMeter.App.App.BossListMaxAge))
        {
            RemindText.Text = ScheduleText.Text = t.OpenBossListShort;
            RemindText.Foreground = ScheduleText.Foreground = (Brush)FindResource("Amber");
            RemindText.ToolTip = ScheduleText.ToolTip = t.OpenBossListHint;
        }
    }

    public void OnLanguageChanged()
    {
        _oneHpAt = 0;
        _vm.LanguageChanged();
        ShowUpdateBanner();
        ApplyHotkeyTips();
        UpdateModeLabel();
        Refresh();
    }

    private Version? _bannerClosedFor; // the banner's ✕: hidden until a newer version comes out or the next start

    /// <summary>The green "Version 0.2.0 is out" banner above the footer, while an update is available (not skipped or closed).</summary>
    private void ShowUpdateBanner()
    {
        var available = _meter.Updates.Available;
        var show = available is not null && available.Version != _bannerClosedFor;
        UpdateBanner.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            var ready = _meter.Updates.Downloaded?.Version == available!.Version; // downloaded: it installs itself soon
            UpdateBannerText.Text = string.Format(ready ? UiText.Current.UpdateBannerReady : UiText.Current.UpdateBanner, available.Version.ToString(3));
        }
    }

    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (!_drag.JustDragged) AppHost.Current.ShowUpdate();
    }

    private void UpdateBannerClose_Click(object sender, RoutedEventArgs e)
    {
        _bannerClosedFor = _meter.Updates.Available?.Version;
        ShowUpdateBanner();
    }

    private void Language_Click(object sender, RoutedEventArgs e) => AppHost.Current.ToggleLanguage();

    /// <summary>Re-reads appearance settings changed in the Settings window.</summary>
    public void ApplyAppearance()
    {
        OpacitySlider.Value = _settings.BackgroundOpacity;
        FrameBackground.Opacity = _settings.BackgroundOpacity;
        ApplyCompact();
        ApplyRowSize(_settings.RowSize);
        ApplyDynamic();
        UpdateModeLabel();
    }

    private bool? _compact;

    /// <summary>
    /// 1 HP: the compact layout (Settings → 1 HP, on by default) — a small portrait, title and clock, a slim boss bar,
    /// tighter margins and rows at 85 %. Switching it later also changes the card's height by about what it saves.
    /// </summary>
    private void ApplyCompact()
    {
        var on = _settings.Compact;
        if (_compact == on) return;
        if (_compact is not null && !_settings.DynamicOverlay) // the dynamic card sizes itself
        {
            Height = Math.Max(MinHeight, Height + (on ? -CompactSaves : CompactSaves));
            _settings.OverlayHeight = Height; // also while hidden (SavePlacement skips a window never shown)
            _settings.Save();
        }
        _compact = on;
        Resources["CardPadding"] = on ? new Thickness(12, 8, 12, 6) : new Thickness(20, 14, 20, 10);
        Resources["HeaderHeight"] = on ? 22.0 : 30.0;
        Resources["ClockFont"] = on ? 18.0 : 27.0;
        Resources["TitleRowMargin"] = on ? new Thickness(0, 2, 0, 0) : new Thickness(0, 8, 0, 0);
        Resources["PortraitBox"] = on ? 44.0 : 74.0;
        Resources["PortraitRing"] = on ? 34.0 : 58.0;
        Resources["PortraitImage"] = on ? 40.0 : 68.0;
        Resources["SkullSize"] = on ? 16.0 : 26.0;
        Resources["TitleTextMargin"] = on ? new Thickness(10, 0, 6, 0) : new Thickness(14, 0, 8, 0);
        Resources["TitleFontSize"] = on ? 17.0 : 25.0;
        Resources["DetailFont"] = on ? 11.0 : 12.5;
        Resources["TopHitFont"] = on ? 16.0 : 25.0;
        Resources["TopHitLabelFont"] = on ? 8.0 : 9.5;
        Resources["BossBarHeight"] = on ? 22.0 : 34.0;
        Resources["BossBarMargin"] = on ? new Thickness(0, 6, 0, 0) : new Thickness(0, 14, 0, 0);
        Resources["BossFont"] = on ? 12.0 : 14.5;
        Resources["PartyMargin"] = on ? new Thickness(0, 6, 0, 0) : new Thickness(0, 12, 0, 0);
        Resources["FooterMargin"] = on ? new Thickness(0, 4, 0, 0) : new Thickness(0, 8, 0, 0);
        Resources["NetMargin"] = on ? new Thickness(0, 3, 12, 0) : new Thickness(0, 6, 12, 0);
        Resources["FooterFont"] = on ? 11.0 : 12.0;
    }

    /// <summary>Roughly the height the compact layout saves with ten rows (header, boss bar, margins, slim rows).</summary>
    public const double CompactSaves = 260;

    /// <summary>
    /// Row size in percent of the original. The height follows it exactly; text, emblems and the number columns shrink
    /// more gently (square root), so the smallest rows stay readable and the names get the room the numbers give up.
    /// </summary>
    public void ApplyRowSize(int percent)
    {
        // 1 HP: compact rows are slim single lines (~26 px at 100 %), like the small meters players know
        var compact = _settings.Compact;
        var k = Math.Clamp(percent, AppSettings.MinRowSize, AppSettings.MaxRowSize) / 100.0 * (compact ? 0.62 : 1);
        var f = Math.Sqrt(k);
        var height = Math.Round(42 * k);
        Resources["RowHeight"] = height;
        Resources["RowMargin"] = new Thickness(0, 0, 0, compact ? 2 : Math.Max(2, Math.Round(6 * k)));
        Resources["RowGlossHeight"] = Math.Round(19 * k);
        Resources["RowNameFont"] = Math.Round(16.5 * f, 1);
        Resources["RowNumberFont"] = Math.Round(16 * f, 1);
        Resources["RowGlyphSize"] = Math.Min(Math.Round(30 * f), height - 2);
        Resources["RowRankSize"] = Math.Min(Math.Round(24 * f), height - 3);
        Resources["RowRankFont"] = Math.Round(13 * f, 1);
        Resources["ColHeaderFont"] = Math.Max(9, Math.Round(11 * f, 1));
        Resources["ColRankWidth"] = new GridLength(Math.Round(30 * f));
        Resources["ColGlyphWidth"] = new GridLength(Math.Round(32 * f));
        Resources["ColDpsWidth"] = new GridLength(Math.Round(104 * f));
        Resources["ColDamageWidth"] = new GridLength(Math.Round(74 * f));
        Resources["ColShareWidth"] = new GridLength(Math.Round(68 * f));
    }

    /// <summary>Ctrl + mouse wheel over the rows: row size in 5 % steps, kept right away.</summary>
    private void Rows_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!NativeMethods.IsCtrlDown()) return;
        e.Handled = true;
        var size = Math.Clamp(_settings.RowSize + Math.Sign(e.Delta) * 5, AppSettings.MinRowSize, AppSettings.MaxRowSize);
        if (size == _settings.RowSize) return;
        _settings.RowSize = size;
        ApplyRowSize(size);
        _settings.Save();
    }

    public void SetClickThrough(bool on)
    {
        _settings.ClickThrough = on;
        _settings.Save();
        ApplyClickThrough();
    }

    private void ApplyClickThrough()
    {
        if (_hwnd == 0) return;
        if (_settings.ClickThrough) NativeMethods.SetExStyle(_hwnd, NativeMethods.WS_EX_TRANSPARENT);
        else NativeMethods.SetExStyle(_hwnd, 0, NativeMethods.WS_EX_TRANSPARENT);
        Frame.BorderBrush = _settings.ClickThrough ? (Brush)FindResource("Line") : (Brush)FindResource("CardFrame");
    }

    private void ApplyLock()
    {
        LockButton.IsChecked = _settings.Locked;
        LockGlyph.Text = _settings.Locked ? "" : "";
        UpdateChrome();
    }

    private void UpdateModeLabel()
    {
        _vm.ModeLabel = _settings.TargetMode == TargetMode.BossOnly ? "BOSS" : "ALL";
        _vm.PartyLabel = _settings.PartyOnly ? UiText.Current.PartyOnlyChip : UiText.Current.EveryoneChip;
    }

    private void EnsureOnScreen()
    {
        var vw = SystemParameters.VirtualScreenWidth;
        var vh = SystemParameters.VirtualScreenHeight;
        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        if (Left + 60 > vl + vw || Left + Width - 60 < vl) Left = vl + 80;
        if (Top + 40 > vt + vh || Top < vt) Top = vt + 160;
    }

    /// <summary>
    /// Keeps the whole card inside the work area of the monitor it sits on (a taller layout must not hang off the
    /// bottom edge). Runs once the window has a handle, when the monitor and its DPI are known.
    /// </summary>
    private void FitToMonitor()
    {
        if (_hwnd == 0) return;
        var area = System.Windows.Forms.Screen.FromHandle(_hwnd).WorkingArea; // physical pixels
        var dpi = VisualTreeHelper.GetDpi(this);
        var left = area.Left / dpi.DpiScaleX;
        var top = area.Top / dpi.DpiScaleY;
        var right = area.Right / dpi.DpiScaleX;
        var bottom = area.Bottom / dpi.DpiScaleY;
        if (_settings.DynamicOverlay)
        {
            // The dynamic card grows down from where it sits: never past the bottom of the screen (the rows scroll then).
            Left = Math.Clamp(Left, left, Math.Max(left, right - Width));
            Top = Math.Clamp(Top, top, Math.Max(top, bottom - 60));
            MaxHeight = Math.Max(120, bottom - Top);
        }
        else
        {
            MaxHeight = double.PositiveInfinity;
            if (Height > bottom - top) Height = Math.Max(MinHeight, bottom - top);
            Left = Math.Clamp(Left, left, Math.Max(left, right - Width));
            Top = Math.Clamp(Top, top, Math.Max(top, bottom - Height));
        }
        Log.Info($"Overlay placed at {Left:0},{Top:0} {Width:0}x{ActualHeight:0} DIP; monitor work area {left:0},{top:0}-{right:0},{bottom:0} DIP (scale {dpi.DpiScaleX})");
    }

    /// <summary>After a drag or resize, and once more on exit (1 HP: so the last place is never lost).</summary>
    public void SavePlacement()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top) || ActualWidth < 1) return;
        _settings.OverlayLeft = Left;
        _settings.OverlayTop = Top;
        _settings.OverlayWidth = Width;
        if (!_settings.DynamicOverlay) _settings.OverlayHeight = Height; // the dynamic card's height follows its rows
        _settings.Save();
        if (_settings.DynamicOverlay) FitToMonitor(); // room to grow below the new place
    }

    // ------------------------------------------------------------ 1 HP: dynamic card

    /// <summary>The fight on the card stays open this long after it ends, then the card folds to one line.</summary>
    private const int FoldAfterMs = 15_000;
    private bool? _expanded;
    private Guid? _endedFight;
    private long _endedAt;

    /// <summary>
    /// The dynamic card (Settings → 1 HP, on by default) sizes itself to its content: one line with your name, combat
    /// power and ping out of combat; title, boss bar and a row per fighter in a fight. Off: the fixed-size card.
    /// </summary>
    private void ApplyDynamic()
    {
        if (_settings.DynamicOverlay)
        {
            MinHeight = 0;
            ClearValue(HeightProperty);
            SizeToContent = SizeToContent.Height;
            ResizeGrip.Cursor = Cursors.SizeWE;
        }
        else
        {
            SizeToContent = SizeToContent.Manual;
            MinHeight = 220;
            MaxHeight = double.PositiveInfinity;
            Height = Math.Max(MinHeight, _settings.OverlayHeight);
            ResizeGrip.Cursor = Cursors.SizeNWSE;
        }
        _expanded = null;
        SetExpanded(WantExpanded(null));
        FitToMonitor();
    }

    /// <summary>Open while a fight of yours runs and for <see cref="FoldAfterMs"/> after it, or while you look at a past fight.</summary>
    private bool WantExpanded(EncounterSnapshot? shown)
    {
        if (!_settings.DynamicOverlay || _saved is not null || _segment is not null) return true;
        if (shown is null) return false;
        if (shown.IsActive)
        {
            _endedFight = null;
            return true;
        }
        if (_endedFight != shown.Id)
        {
            _endedFight = shown.Id;
            _endedAt = Environment.TickCount64;
        }
        return Environment.TickCount64 - _endedAt < FoldAfterMs;
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded) return;
        _expanded = expanded;
        if (!expanded) UpdateMini();
        UpdateChrome();
    }

    /// <summary>What shows of the card: the folded line or the fight, the toolbar while the mouse is on it.</summary>
    private void UpdateChrome()
    {
        var hover = IsMouseOver;
        var open = _expanded != false;
        var dynamic = _settings.DynamicOverlay;
        Toolbar.Visibility = hover ? Visibility.Visible : Visibility.Collapsed;
        LabelPanel.Visibility = !hover && open ? Visibility.Visible : Visibility.Collapsed;
        MiniLine.Visibility = !hover && !open ? Visibility.Visible : Visibility.Collapsed;
        ClockText.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        TitleRow.Visibility = PartyRow.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        // The dynamic card always keeps the boss timers and reminders; status, links and network show on hover.
        Footer.Visibility = open || dynamic ? Visibility.Visible : Visibility.Collapsed;
        RemindRow.Visibility = dynamic ? Visibility.Visible : Visibility.Collapsed;
        StatusRow.Visibility = NetRow.Visibility = !dynamic || hover ? Visibility.Visible : Visibility.Collapsed;
        StatusTimers.Visibility = ScheduleText.Visibility = dynamic ? Visibility.Collapsed : Visibility.Visible;
        ResizeGrip.Visibility = _settings.Locked || (dynamic && !hover) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The folded line: your name and combat power (the game's party list, the last one known, or the site).</summary>
    private void UpdateMini()
    {
        var t = UiText.Current;
        var name = _meter.Tracker.SelfName;
        MiniName.Text = string.IsNullOrEmpty(name) ? t.MiniWaiting : name;
        long power = 0;
        if (!string.IsNullOrEmpty(name))
        {
            power = _meter.Tracker.PowerOf(name);
            var known = _settings.Powers.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase));
            if (power > 0 && known.Value != power)
            {
                if (known.Key is not null) _settings.Powers.Remove(known.Key);
                _settings.Powers[name] = power;
                _settings.Save();
            }
            if (power <= 0) power = known.Value;
            if (power <= 0) power = _meter.Cloud.GearFor(name, _meter.Tracker.SelfServerId)?.CombatPower ?? 0;
        }
        MiniPower.Text = power > 0 ? power.ToString("#,0", t.Culture) : "";
        _meter.Stream.SetIdleLine(string.IsNullOrEmpty(name) ? "" : name, MiniPower.Text, MiniNetText.Text);
        MiniPowerBox.Visibility = power > 0 ? Visibility.Visible : Visibility.Collapsed;
        MiniPowerBox.ToolTip = t.MiniPowerTip;
    }

    // ------------------------------------------------------------ handlers

    private void Resize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        if (!_settings.DynamicOverlay) Height = Math.Max(MinHeight, Height + e.VerticalChange); // dynamic: the rows set the height
    }

    private void Resize_DragCompleted(object sender, DragCompletedEventArgs e) => SavePlacement();

    private void OpenBreakdown(uint actorId)
    {
        if (_saved is { } saved) AppHost.Current.ShowBreakdown(saved.View, actorId);
        else if (_vm.SegmentId is { } seg) AppHost.Current.ShowBreakdown(seg, actorId);
    }

    private EncounterSnapshot? ShownSummary() =>
        _saved is { } saved
            ? saved.Record.Summary with { Title = saved.Title }
            : _vm.SegmentId is { } seg ? _meter.Tracker.Snapshot(seg, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) : null;

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        if (_drag.JustDragged) return; // the press moved the window, it was not a click
        if (_death is { } d && _vm.SegmentId == d.Id) return; // a death recap has no breakdown
        if ((sender as FrameworkElement)?.DataContext is RowViewModel row) OpenBreakdown(row.ActorId);
    }

    private void Row_RightClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RowViewModel row) return;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender };
        var open = new MenuItem { Header = UiText.Current.OpenBreakdown };
        open.Click += (_, _) => OpenBreakdown(row.ActorId);
        var copyMe = new MenuItem { Header = string.Format(UiText.Current.CopyPlayer, row.Name) };
        copyMe.Click += (_, _) => CopyChat(row.ActorId);
        var copyAll = new MenuItem { Header = UiText.Current.CopyParty };
        copyAll.Click += (_, _) => CopyChat(null);
        menu.Items.Add(open);
        menu.Items.Add(new Separator());
        menu.Items.Add(copyMe);
        menu.Items.Add(copyAll);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void CopyChat(uint? actorId)
    {
        if (ShownSummary() is not { } snap) return;
        AppHost.CopyText(actorId is { } id ? ChatLine.Player(snap, id) : ChatLine.Party(snap));
    }

    private void Details_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Rows.Count == 0) return;
        var target = _vm.Rows.FirstOrDefault(r => r.IsSelf) ?? _vm.Rows[0];
        OpenBreakdown(target.ActorId);
    }

    /// <summary>1 HP: only my party ⇄ everyone around (outside instances).</summary>
    private void Party_Click(object sender, RoutedEventArgs e)
    {
        _settings.PartyOnly = !_settings.PartyOnly;
        _meter.ApplySettings();
        UpdateModeLabel();
        Refresh();
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        _settings.TargetMode = _settings.TargetMode == TargetMode.BossOnly ? TargetMode.All : TargetMode.BossOnly;
        _meter.ApplySettings();
        UpdateModeLabel();
    }

    // ------------------------------------------------------------ fight picker

    /// <summary>One selectable fight: a segment of this session or a fight saved on disk.</summary>
    private sealed record PickItem(
        string Title, DateTimeOffset Start, long CombatMs, EncounterEndReason Reason, bool Active,
        double SelfDps, int Place, double PartyDps, Guid? Segment, HistoryEntry? Saved, ImageSource? Portrait = null);

    private const int SavedInMenu = 40;

    /// <summary>Newest first: this session's segments, then saved fights not already in the session list.</summary>
    private List<PickItem> BuildFightList(int maxSaved)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var list = new List<PickItem>();
        var sessionStarts = new HashSet<long>();
        foreach (var s in _meter.Tracker.Segments())
        {
            var snap = _meter.Tracker.Snapshot(s.Id, now);
            var selfIndex = snap?.Combatants.ToList().FindIndex(c => c.IsSelf) ?? -1;
            var self = selfIndex >= 0 ? snap!.Combatants[selfIndex] : null;
            list.Add(new PickItem(s.Title, s.StartedAt, s.CombatMs, s.Reason, s.IsActive,
                self?.Dps ?? 0, selfIndex + 1, s.TotalDamage / Math.Max(1.0, s.CombatMs / 1000.0), s.Id, null,
                snap is null ? null : _meter.PortraitOf(snap)));
            sessionStarts.Add(s.StartedAt.ToUnixTimeSeconds());
        }
        foreach (var h in _meter.History.List(maxSaved + sessionStarts.Count))
        {
            if (sessionStarts.Contains(h.StartedAt.ToUnixTimeSeconds())) continue;
            list.Add(new PickItem(_meter.DisplayTitle(h), h.StartedAt, h.CombatMs, h.Reason, false,
                h.SelfDps, h.SelfPlace, h.PartyDps, null, h, _meter.PortraitOf(h)));
            if (list.Count(i => i.Saved is not null) >= maxSaved) break;
        }
        return list;
    }

    private bool IsSelected(PickItem item) =>
        item.Saved is { } h ? _saved?.Entry.FileName == h.FileName : _saved is null && _segment == item.Segment;

    private void SelectLive()
    {
        _saved = null;
        _segment = null;
        Refresh();
    }

    private DateTimeOffset _selectedAt = DateTimeOffset.MinValue;

    private void Select(PickItem item)
    {
        _selectedAt = DateTimeOffset.Now;
        if (item.Saved is { } h)
        {
            if (_meter.History.Load(h.Path) is not { } record)
            {
                AppHost.Current.ShowHistory(); // file vanished or is unreadable: let the history window refresh
                return;
            }
            _saved = new SavedFight(h, record, new RecordFightView(record), _meter.DisplayTitle(h));
            _segment = null;
        }
        else
        {
            _saved = null;
            _segment = item.Segment;
        }
        Refresh();
    }

    private void Segments_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom, MinWidth = 380 };
        var live = new MenuItem
        {
            Header = PickHeader(UiText.Current.Live, UiText.Current.LiveHint, null, 0, EncounterEndReason.None, active: true, partyDps: 0, portrait: null),
            IsCheckable = true,
            IsChecked = _saved is null && _segment is null,
        };
        live.Click += (_, _) => SelectLive();
        menu.Items.Add(live);

        var items = BuildFightList(SavedInMenu);
        string? section = null;
        foreach (var item in items)
        {
            var group = item.Saved is null ? UiText.Current.SessionSection : DayLabel(item.Start);
            if (group != section)
            {
                section = group;
                menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem
                {
                    Header = new TextBlock { Text = group, Style = (Style)FindResource("SectionLabel") },
                    IsEnabled = false,
                });
            }
            var meta = $"{item.Start:HH:mm} · {Format.Clock(item.CombatMs)}";
            var row = new MenuItem
            {
                Header = PickHeader(item.Title, meta, item.SelfDps > 0 ? item.SelfDps : null, item.Place, item.Reason, item.Active, item.PartyDps, item.Portrait),
                IsCheckable = true,
                IsChecked = IsSelected(item),
            };
            var pick = item;
            row.Click += (_, _) => Select(pick);
            menu.Items.Add(row);
        }
        if (items.Count == 0)
            menu.Items.Add(new MenuItem { Header = UiText.Current.NoFights, IsEnabled = false });

        menu.Items.Add(new Separator());
        var total = _meter.History.List().Count;
        var history = new MenuItem { Header = string.Format(UiText.Current.AllSaved, total) };
        history.Click += (_, _) => AppHost.Current.ShowHistory();
        menu.Items.Add(history);
        menu.IsOpen = true;
    }

    private static string DayLabel(DateTimeOffset t)
    {
        var day = t.LocalDateTime.Date;
        if (day == DateTime.Today) return UiText.Current.SavedToday;
        if (day == DateTime.Today.AddDays(-1)) return UiText.Current.SavedYesterday;
        return string.Format(UiText.Current.SavedOn, day.ToString("dd MMM yyyy", UiText.Current.Culture).ToUpperInvariant());
    }

    /// <summary>Two-line menu row: boss portrait, result mark, title, time · length, and your DPS with your place badge.</summary>
    private FrameworkElement PickHeader(string title, string meta, double? selfDps, int place, EncounterEndReason reason, bool active, double partyDps,
        ImageSource? portrait)
    {
        var grid = new Grid { MinWidth = 360 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new BossPortrait { Source = portrait, Width = 28, Height = 28, HorizontalAlignment = HorizontalAlignment.Left });

        var (mark, markBrush) = active ? ("●", "Red") : reason switch
        {
            EncounterEndReason.Kill => ("✔", "Green"),
            EncounterEndReason.Wipe => ("✖", "Red"),
            _ => ("•", "TextMute"),
        };
        var markText = new TextBlock { Text = mark, Foreground = (Brush)FindResource(markBrush), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(markText, 1);
        grid.Children.Add(markText);

        var text = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
        text.Children.Add(new TextBlock
        {
            Text = title, FontWeight = FontWeights.SemiBold, FontSize = 12.5, MaxWidth = 230,
            TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)FindResource("Text"),
        });
        text.Children.Add(new TextBlock
        {
            Text = meta, FontSize = 10.5, FontFamily = (FontFamily)FindResource("NumberFont"), Foreground = (Brush)FindResource("TextMute"),
        });
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (selfDps is { } dps)
        {
            right.Children.Add(new TextBlock
            {
                Text = Format.Compact(dps) + "/s", FontFamily = (FontFamily)FindResource("NumberFont"), FontSize = 12.5,
                FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("GoldBright"), VerticalAlignment = VerticalAlignment.Center,
            });
            if (place > 0)
            {
                var (bg, fg, border) = RankBrushes.For(place);
                right.Children.Add(new Border
                {
                    Width = 20, Height = 20, Margin = new Thickness(7, 0, 0, 0), CornerRadius = new CornerRadius(5),
                    Background = bg, BorderBrush = border, BorderThickness = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = place.ToString(), FontFamily = (FontFamily)FindResource("NumberFont"), FontSize = 11,
                        FontWeight = FontWeights.Bold, Foreground = fg,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    },
                });
            }
        }
        else if (partyDps > 0)
        {
            right.Children.Add(new TextBlock
            {
                Text = UiText.Current.Party + " " + Format.Compact(partyDps) + "/s", FontFamily = (FontFamily)FindResource("NumberFont"),
                FontSize = 11.5, Foreground = (Brush)FindResource("TextDim"), VerticalAlignment = VerticalAlignment.Center,
            });
        }
        Grid.SetColumn(right, 3);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>Mouse wheel over the title steps through fights: down = older, up = newer, past the newest = live.</summary>
    private void Title_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var items = BuildFightList(SavedInMenu);
        var index = items.FindIndex(IsSelected); // -1 = live
        if (_saved is null && _segment is null) index = -1;
        index += e.Delta < 0 ? 1 : -1;
        if (index < 0) SelectLive();
        else if (index < items.Count) Select(items[index]);
        e.Handled = true;
    }

    /// <summary>Called by the history window to show a saved fight in the overlay.</summary>
    public void ShowSaved(HistoryEntry entry)
    {
        Select(new PickItem(entry.Title, entry.StartedAt, entry.CombatMs, entry.Reason, false,
            entry.SelfDps, entry.SelfPlace, entry.PartyDps, null, entry));
    }

    private void Timers_Click(object sender, RoutedEventArgs e)
    {
        if (!_drag.JustDragged) AppHost.Current.ShowBossTimers();
    }

    private long _timersLabelAt;

    /// <summary>The next respawn among watched bosses (or all, when none is watched): "Gartua 12:34".</summary>
    private void UpdateTimersLabel()
    {
        _timersLabelAt = Environment.TickCount64;
        var now = DateTimeOffset.Now;
        var timers = _meter.Timers.ForServer(_meter.Timers.CurrentServer).Where(t => t.NpcCode > 0).ToList();
        var pool = timers.Any(t => t.Watch) ? timers.Where(t => t.Watch) : timers;
        var next = pool.Where(t => !t.AliveNow && t.NextSpawn > now).MinBy(t => t.NextSpawn);
        if (next?.NextSpawn is not { } at)
        {
            _vm.TimersLabel = UiText.Current.TimersButton;
            return;
        }
        var name = next.NpcCode > 0 ? _meter.Data.NpcName(next.NpcCode) : string.Format(UiText.Current.UnknownSlotBoss, -next.NpcCode % 100);
        var shortName = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? name;
        var left = at - now;
        _vm.TimersLabel = $"{shortName} {(left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"mm\:ss"))}";
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => AppHost.Current.RestartMeter();

    /// <summary>Back to the live view (after a restart of the meter: empty until the next hit).</summary>
    public void ShowLive()
    {
        _segment = null;
        _saved = null;
        Refresh();
    }

    /// <summary>Tooltips that name a hotkey the user can change.</summary>
    public void ApplyHotkeyTips() =>
        RestartButton.ToolTip = string.Format(UiText.Current.TipRestart, _settings.HotkeyReset);

    private void Lock_Click(object sender, RoutedEventArgs e)
    {
        _settings.Locked = LockButton.IsChecked == true;
        _settings.Save();
        ApplyLock();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => AppHost.Current.ShowSettings();

    private void Hide_Click(object sender, RoutedEventArgs e) => AppHost.Current.SetOverlayVisible(false);

    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FrameBackground is null) return;
        FrameBackground.Opacity = e.NewValue;
        _settings.BackgroundOpacity = Math.Round(e.NewValue, 2);
    }

    protected override void OnClosed(EventArgs e)
    {
        SavePlacement();
        base.OnClosed(e);
    }
}
