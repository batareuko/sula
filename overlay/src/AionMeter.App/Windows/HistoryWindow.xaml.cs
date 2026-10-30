using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AionMeter.App.Services;
using AionMeter.App.ViewModels;
using AionMeter.Core;
using AionMeter.Core.Combat;
using AionMeter.Core.Storage;

namespace AionMeter.App.Windows;

public partial class HistoryWindow : Window
{
    private static UiText T => UiText.Current;
    private readonly MeterService _meter;
    private readonly Controls.DragAnywhere _drag;
    private HistoryStore Store => _meter.History;
    private FightRecord? _record;
    private RecordFightView? _view;
    private HistoryEntry? _entry;

    /// <summary>One line of the list: a saved fight, or (1 HP) a dungeon run or a PvP session.</summary>
    public sealed record FightItem(
        HistoryEntry? Entry, string Title, string Meta, string Dps, string When, string ResultGlyph, Brush ResultBrush,
        int Place, Brush PlaceBg, Brush PlaceFg, Brush PlaceBorder, Visibility PlaceVisibility) : INotifyPropertyChanged
    {
        private ImageSource? _portrait;

        public DungeonRun? Run { get; init; }
        public PvpSession? Pvp { get; init; }
        /// <summary>Fights of a run sit under it.</summary>
        public Thickness Indent { get; init; }
        /// <summary>Drawn over the empty portrait of a run or a PvP session.</summary>
        public string Badge { get; init; } = "";
        public Brush? TitleBrush { get; init; }
        public string Key => Entry?.FileName ?? Run?.Id.ToString() ?? Pvp?.Id.ToString() ?? "";

        /// <summary>Boss portrait; filled in later when it was still downloading.</summary>
        public ImageSource? Portrait
        {
            get => _portrait;
            set
            {
                if (ReferenceEquals(_portrait, value)) return;
                _portrait = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Portrait)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        // The list box tracks its items by hash: identity keeps it stable when the portrait arrives.
        public bool Equals(FightItem? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }

    public HistoryWindow(MeterService meter)
    {
        _meter = meter;
        InitializeComponent();
        WindowMemory.Track(this, "history");
        _drag = Controls.DragAnywhere.Attach(this);
        Loaded += (_, _) => Reload();
        Store.Changed += OnStoreChanged;
        _meter.Portraits.PortraitReady += OnPortraitReady;
        Closed += (_, _) =>
        {
            Store.Changed -= OnStoreChanged;
            _meter.Portraits.PortraitReady -= OnPortraitReady;
        };
    }

    private void OnStoreChanged() => Dispatcher.BeginInvoke(() => Reload(keepSelection: true));

    private void OnPortraitReady() => Dispatcher.BeginInvoke(() =>
    {
        if (Fights.ItemsSource is not List<FightItem> items) return;
        foreach (var item in items)
            if (item.Entry is { } entry) item.Portrait ??= _meter.PortraitOf(entry);
        if (_entry is not null) DetailPortrait.Source ??= _meter.PortraitOf(_entry);
    });

    public void Reload() => Reload(keepSelection: false);

    private void Reload(bool keepSelection)
    {
        var selected = (Fights.SelectedItem as FightItem)?.Key;
        var all = Store.List();
        var kills = FilterKills.IsChecked == true;
        List<FightItem> items;
        int total;
        if (FilterPvp.IsChecked == true)
        {
            var sessions = _meter.PvpSessions();
            total = sessions.Count;
            items = sessions.Where(p => !kills || p.Kills > 0).Select(PvpItem).ToList();
        }
        else if (FilterDungeons.IsChecked == true)
        {
            // each run, then the fights saved during it; dungeon fights outside any known run after them
            var runs = _meter.DungeonRuns();
            var fights = all.Where(_meter.InDungeon).Where(e => !kills || e.Reason == EncounterEndReason.Kill).ToList();
            total = runs.Count;
            items = new List<FightItem>();
            var used = new HashSet<string>();
            foreach (var r in runs.Where(r => !kills || r.BossesKilled > 0))
            {
                items.Add(RunItem(r));
                var from = DateTimeOffset.FromUnixTimeMilliseconds(r.EnteredMs).AddSeconds(-5);
                var to = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(r.LeftMs, r.LastBossKillMs)).AddSeconds(5);
                foreach (var e in fights.Where(e => e.StartedAt >= from && e.StartedAt <= to && used.Add(e.FileName)))
                    items.Add(FightItemFor(e) with { Indent = new Thickness(22, 0, 0, 0) });
            }
            items.AddRange(fights.Where(e => !used.Contains(e.FileName)).Select(FightItemFor));
        }
        else
        {
            IEnumerable<HistoryEntry> shown = all;
            if (FilterBosses.IsChecked == true) shown = shown.Where(e => e.IsBoss && !_meter.InDungeon(e));
            if (FilterMobs.IsChecked == true) shown = shown.Where(e => !e.IsBoss && !_meter.InDungeon(e));
            if (kills) shown = shown.Where(e => e.Reason == EncounterEndReason.Kill);
            total = all.Count;
            items = shown.Select(FightItemFor).ToList();
        }

        var count = items.Count(i => i.Indent.Left == 0);
        CountText.Text = count == total ? string.Format(T.SavedCount, total) : string.Format(T.ShownOf, count, total);
        Fights.ItemsSource = items;

        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (items.Count == 0)
        {
            Party.ItemsSource = null;
            DetailTitle.Text = DetailMeta.Text = "";
            DetailPortrait.Source = null;
            return;
        }
        var again = keepSelection && selected is not null ? items.FindIndex(i => i.Key == selected) : -1;
        Fights.SelectedIndex = again >= 0 ? again : 0;
    }

    private FightItem FightItemFor(HistoryEntry e)
    {
        var (bg, fg, border) = RankBrushes.For(e.SelfPlace);
        var dps = e.SelfDps > 0 ? Format.Compact(e.SelfDps) + "/s" : T.Party + " " + Format.Compact(e.PartyDps) + "/s";
        return new FightItem(
            e,
            _meter.DisplayTitle(e),
            $"{ZoneText(e.Zone)} · {Format.Clock(e.CombatMs)} · {e.Players} {T.PlayersShort}",
            dps,
            Relative(e.StartedAt),
            e.Reason switch { EncounterEndReason.Kill => "✔", EncounterEndReason.Wipe => "✖", _ => "•" },
            e.Reason switch { EncounterEndReason.Kill => Brush("Green"), EncounterEndReason.Wipe => Brush("Red"), _ => Brush("TextMute") },
            e.SelfPlace, bg, fg, border, e.SelfPlace > 0 ? Visibility.Visible : Visibility.Collapsed)
        {
            Portrait = _meter.PortraitOf(e),
        };
    }

    /// <summary>1 HP: a dungeon run — the zone, how long it took, bosses, deaths; your DPS over the run.</summary>
    private FightItem RunItem(DungeonRun r)
    {
        var (bg, fg, border) = RankBrushes.For(0);
        var me = r.Members.FirstOrDefault(m => m.IsSelf);
        return new FightItem(null, string.Format(T.RunTitle, _meter.DisplayZone(r.Zone) ?? r.Zone),
            string.Format(T.RunDetail, Format.Clock(r.ClearMs), r.BossesKilled, r.BossFights, r.Deaths),
            me is { Dps: > 0 } ? Format.Compact(me.Dps) + "/s" : "",
            Relative(DateTimeOffset.FromUnixTimeMilliseconds(r.EnteredMs).ToLocalTime()),
            r.Cleared ? "✔" : "•", r.Cleared ? Brush("Green") : Brush("TextMute"),
            0, bg, fg, border, Visibility.Collapsed)
        {
            Run = r,
            Badge = "⛨",
            TitleBrush = Brush("GoldBright"),
        };
    }

    /// <summary>1 HP: a PvP session — kills and deaths, how long, damage dealt and taken.</summary>
    private FightItem PvpItem(PvpSession p)
    {
        var (bg, fg, border) = RankBrushes.For(0);
        return new FightItem(null, string.Format(T.PvpTitle, p.Kills, p.Deaths),
            $"{p.Opponents.Count} {T.PvpOpponentsShort} · {Format.Clock(p.DurationMs)} · {Format.Compact(p.Taken)} {T.PvpTakenShort}",
            Format.Compact(p.Dealt),
            Relative(DateTimeOffset.FromUnixTimeMilliseconds(p.StartMs).ToLocalTime()),
            p.Kills > p.Deaths ? "✔" : p.Deaths > 0 ? "✖" : "•",
            p.Kills > p.Deaths ? Brush("Green") : p.Deaths > 0 ? Brush("Red") : Brush("TextMute"),
            0, bg, fg, border, Visibility.Collapsed)
        {
            Pvp = p,
            Badge = "⚔",
        };
    }

    private Brush Brush(string key) => (Brush)FindResource(key);

    private string ZoneText(string? zone) =>
        _meter.DisplayZone(zone) is { } z ? (z == "Open world" ? T.OpenWorld : z) : "—";

    private static string ReasonText(EncounterEndReason reason) => reason switch
    {
        EncounterEndReason.Kill => T.Kill,
        EncounterEndReason.Wipe => T.Wipe,
        EncounterEndReason.None => T.InProgress,
        _ => T.Ended,
    };

    private static string Relative(DateTimeOffset t)
    {
        var ago = DateTimeOffset.Now - t;
        if (ago.TotalMinutes < 1) return T.JustNow;
        if (ago.TotalHours < 1) return string.Format(T.MinutesAgo, (int)ago.TotalMinutes);
        if (ago.TotalDays < 1) return string.Format(T.HoursAgo, (int)ago.TotalHours);
        if (ago.TotalDays < 7) return t.ToString("ddd HH:mm", T.Culture);
        return t.ToString("dd MMM", T.Culture);
    }

    private void Fights_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (Fights.SelectedItem is not FightItem item) return;
        if (item.Run is { } run) { ShowRun(item, run); return; }
        if (item.Pvp is { } pvp) { ShowPvp(item, pvp); return; }
        if (item.Entry is not { } entry) return;
        _record = Store.Load(entry.Path);
        if (_record is null) return;
        _entry = entry;
        _view = new RecordFightView(_record);
        ShowInOverlayButton.Visibility = Visibility.Visible;
        var s = _record.Summary;
        DetailTitle.Text = item.Title;
        DetailPortrait.Source = item.Portrait;
        DetailMeta.Text = $"{s.StartedAt.ToString("dd MMM yyyy HH:mm", T.Culture)} · {Format.ClockPrecise(s.CombatMs)} · " +
                          $"{Format.Compact(s.TotalDamage)} {T.DamageWord} · {Format.Compact(s.PartyDps)}/s {T.Party} · {ReasonText(s.Reason)}";
        // 1 HP: against the last try on the same boss — faster or slower, more or less DPS
        if (FightExport.CompareWithPrevious(entry, Store.List()) is { } cmp)
            DetailMeta.Text += "\n" + string.Format(T.CompareLast, cmp.Before.StartedAt.ToString("dd MMM HH:mm", T.Culture),
                Signed(cmp.PartyDpsChange), (cmp.CombatMsChange >= 0 ? "+" : "−") + Format.ClockPrecise(Math.Abs(cmp.CombatMsChange)),
                cmp.SelfDpsChange is { } me ? Signed(me) : "—");
        var vm = new OverlayViewModel();
        vm.Apply(s, 50, relativeToTop: true);
        Party.ItemsSource = vm.Rows;
    }

    /// <summary>1 HP: a run in the detail pane — each member's damage, DPS over the run and deaths.</summary>
    private void ShowRun(FightItem item, DungeonRun r)
    {
        _entry = null;
        _view = null;
        ShowInOverlayButton.Visibility = Visibility.Collapsed;
        DetailTitle.Text = item.Title;
        DetailPortrait.Source = null;
        var at = DateTimeOffset.FromUnixTimeMilliseconds(r.EnteredMs).ToLocalTime();
        DetailMeta.Text = $"{at.ToString("dd MMM yyyy HH:mm", T.Culture)} · " +
                          string.Format(T.RunDetail, Format.Clock(r.ClearMs), r.BossesKilled, r.BossFights, r.Deaths) +
                          (r.Cleared ? " · " + T.RunCleared : "");
        var total = Math.Max(1, r.Members.Sum(m => m.Damage));
        var rows = r.Members.Select((m, i) => new CombatantSnapshot((uint)(i + 1), m.Name, m.Class, m.IsSelf, m.Damage, m.Dps,
            (double)m.Damage / total, 0, 0, 0, 0)).ToList();
        var snap = new EncounterSnapshot(r.Id, item.Title, r.Zone, at, r.ClearMs, Math.Max(1_000, r.ClearMs), false,
            EncounterEndReason.ZoneChange, total, rows.Sum(x => x.Dps), null, rows);
        var vm = new OverlayViewModel();
        vm.Apply(snap, 50, relativeToTop: true);
        foreach (var row in vm.Rows)
            if (r.Members.FirstOrDefault(m => m.Name == row.Name) is { Deaths: > 0 } m) row.Name += "  ☠" + m.Deaths;
        Party.ItemsSource = vm.Rows;
    }

    /// <summary>1 HP: a PvP session in the detail pane — per opponent: dealt to them, taken from them, the outcome.</summary>
    private void ShowPvp(FightItem item, PvpSession p)
    {
        _entry = null;
        _view = null;
        ShowInOverlayButton.Visibility = Visibility.Collapsed;
        DetailTitle.Text = item.Title;
        DetailPortrait.Source = null;
        var at = DateTimeOffset.FromUnixTimeMilliseconds(p.StartMs).ToLocalTime();
        DetailMeta.Text = $"{at.ToString("dd MMM yyyy HH:mm", T.Culture)} · {Format.Clock(p.DurationMs)} · " +
                          string.Format(T.PvpDetail, Format.Compact(p.Dealt), Format.Compact(p.Taken), Format.Compact(p.Burst));
        var seconds = Math.Max(1, p.DurationMs) / 1000.0;
        var top = Math.Max(1, p.Opponents.Count == 0 ? 1 : p.Opponents.Max(o => o.Dealt + o.Taken));
        var rows = p.Opponents.Select(o => new CombatantSnapshot(o.ActorId, o.Name, o.Class, false, o.Dealt + o.Taken,
            (o.Dealt + o.Taken) / seconds, (double)(o.Dealt + o.Taken) / top, o.HitsDealt, 0, 0, o.Taken, o.ServerId)).ToList();
        var snap = new EncounterSnapshot(p.Id, item.Title, null, at, p.DurationMs, Math.Max(1_000, p.DurationMs), false,
            EncounterEndReason.Manual, p.Dealt, p.Dealt / seconds, null, rows);
        var vm = new OverlayViewModel();
        vm.Apply(snap, 50, relativeToTop: true);
        foreach (var row in vm.Rows)
        {
            if (p.Opponents.FirstOrDefault(o => o.ActorId == row.ActorId) is not { } o) continue;
            row.Dps = Format.Compact(o.Dealt);
            row.Damage = Format.Compact(o.Taken);
            row.Share = o.Defeated ? "☠" : o.KilledYou ? "⚔" : "—";
            var server = _meter.Data.ServerName(o.ServerId);
            if (!string.IsNullOrEmpty(server)) row.Name += " · " + server;
        }
        Party.ItemsSource = vm.Rows;
    }

    private void Kills_Toggle(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        FilterKills.IsChecked = FilterKills.IsChecked != true;
        if (IsLoaded) Reload();
    }

    private void Member_Click(object sender, MouseButtonEventArgs e)
    {
        if (_drag.JustDragged || _view is null || (sender as FrameworkElement)?.DataContext is not RowViewModel row) return;
        AppHost.Current.ShowBreakdown(_view, row.ActorId);
    }

    private void ShowInOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (_entry is not null) AppHost.Current.ShowSavedInOverlay(_entry);
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded) Reload();
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Fights.SelectedItem is not FightItem { Entry: { } entry } item) return;
        var answer = MessageBox.Show(this, string.Format(T.DeleteConfirm, item.Title, entry.StartedAt.ToString("dd MMM HH:mm", T.Culture)), AppInfo.Name,
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        Store.Delete(entry.Path);
    }

    private static string Signed(double change) => (change >= 0 ? "+" : "−") + Format.Percent(Math.Abs(change), 0);

    /// <summary>1 HP: the selected fight as a spreadsheet (CSV, one row per player) or as the saved fight file (JSON).</summary>
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_record is null || _entry is null || Fights.SelectedItem is not FightItem { Entry: not null } item) return;
        var name = string.Concat($"{item.Title} {_entry.StartedAt:yyyy-MM-dd HHmm}".Split(System.IO.Path.GetInvalidFileNameChars()));
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = name,
            Filter = "CSV (Excel)|*.csv|JSON|*.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (dialog.FilterIndex == 2) System.IO.File.Copy(_entry.Path, dialog.FileName, overwrite: true);
            else System.IO.File.WriteAllText(dialog.FileName, FightExport.Csv(_record.Summary), new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", Store.Directory) { UseShellExecute = true });

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
