using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AionMeter.App.Controls;
using AionMeter.App.Services;
using AionMeter.Core.OneHp;

namespace AionMeter.App.Windows;

/// <summary>
/// 1 HP: the daily and weekly checklist (tray ✅, Ctrl+Shift+J), per character. What the game sends fills itself in:
/// your tickets and energy (the resource packets) and this week's dungeon runs the meter saw; the rest you tick, and it
/// clears itself at the daily (07:00 UTC) and weekly (Wednesday) reset.
/// </summary>
public sealed class ChecklistWindow : Window
{
    private readonly MeterService _meter;
    private readonly Checklist _list;
    private readonly StackPanel _body = new() { Margin = new Thickness(14, 10, 14, 14) };
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<int, string> _dungeonsEn = LoadDungeonNames();
    private string _shownKey = "";

    private static readonly Brush Panel = Frozen(Color.FromRgb(0x14, 0x1A, 0x29));
    private static readonly Brush Line = Frozen(Color.FromArgb(0x30, 0x6E, 0x8B, 0xFF));
    private static readonly Brush Muted = Frozen(Color.FromRgb(0x8A, 0x94, 0xA6));
    private static readonly Brush Text = Frozen(Color.FromRgb(0xE4, 0xEA, 0xF4));
    private static readonly Brush Green = Frozen(Color.FromRgb(0x5E, 0xE0, 0x8C));
    private static readonly Brush Cyan = Frozen(Color.FromRgb(0x3F, 0xD0, 0xE0));
    private static readonly Brush Track = Frozen(Color.FromRgb(0x24, 0x2C, 0x3E));

    public ChecklistWindow(MeterService meter)
    {
        _meter = meter;
        _list = Checklist.Load(System.IO.Path.Combine(MeterService.DataDirectory, "checklist.json"));
        Title = "1 HP — " + UiText.Current.ChecklistTitle;
        Width = 980;
        Height = 760;
        MinWidth = 560;
        MinHeight = 400;
        Background = Frozen(Color.FromRgb(0x0E, 0x13, 0x20));
        FontFamily = new FontFamily("Segoe UI");
        Foreground = Text;
        WindowMemory.Track(this, "checklist");
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _body };
        _timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); _timer.Start(); } else _timer.Stop(); };
        SizeChanged += (_, _) => { _shownKey = ""; Refresh(); };
    }

    private string Me => _meter.Settings.LastSelfName ?? "";

    private Dictionary<string, int[]> Marks
    {
        get
        {
            var all = _meter.Settings.ChecklistMarks;
            if (!all.TryGetValue(Me, out var mine)) all[Me] = mine = new Dictionary<string, int[]>();
            return mine;
        }
    }

    /// <summary>The checklist anew when something it shows has changed (a tick, a ticket, a run, a reset, the character).</summary>
    public void Refresh()
    {
        var now = DateTimeOffset.UtcNow;
        var res = Resources();
        var runs = RunsThisWeek(now);
        var key = $"{Me}|{UiText.Current.Code}|{ActualWidth >= 820}|{Checklist.PeriodStart(false, now)}|" +
                  string.Join(",", res.Select(kv => $"{kv.Key}={kv.Value}")) + "|" + string.Join(",", runs.Select(kv => $"{kv.Key}={kv.Value}")) + "|" +
                  string.Join(",", Marks.Select(kv => $"{kv.Key}={string.Join(":", kv.Value)}")) + "|" + now.ToString("HH:mm");
        if (key == _shownKey) return;
        _shownKey = key;
        Build(now, res, runs);
    }

    private void Build(DateTimeOffset now, Dictionary<int, string> res, Dictionary<string, int> runs)
    {
        var t = UiText.Current;
        _body.Children.Clear();
        if (_list.Daily.Count == 0)
        {
            _body.Children.Add(new TextBlock { Text = t.ChecklistNoData, Foreground = Muted });
            return;
        }

        // who: the character the meter knows (the game's own name); the marks are kept per character
        var head = new DockPanel { Margin = new Thickness(2, 0, 2, 10) };
        var who = new StackPanel();
        who.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(Me) ? t.ChecklistNoCharacter : Me, FontSize = 22, FontWeight = FontWeights.SemiBold });
        var inGame = _meter.Tracker.SelfName is { Length: > 0 };
        who.Children.Add(new TextBlock { Text = inGame ? "● " + t.ChecklistInGame : t.ChecklistNotInGame, Foreground = inGame ? Green : Muted, Margin = new Thickness(0, 2, 0, 0) });
        head.Children.Add(who);
        _body.Children.Add(head);

        // what the game sent: tickets, keys, energy
        var from = Card(t.ChecklistFromGame, null);
        if (res.Count == 0) from.Children.Add(new TextBlock { Text = t.ChecklistNoResources, Foreground = Muted, TextWrapping = TextWrapping.Wrap });
        else
        {
            var wrap = new WrapPanel();
            foreach (var (id, amount) in res)
            {
                var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 22, 4) };
                cell.Children.Add(new ResourceIcon { ResourceId = id, Width = 22, Height = 22, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
                var text = new StackPanel();
                text.Children.Add(new TextBlock { Text = t.ResourceName(id), Foreground = Muted, FontSize = 11.5 });
                text.Children.Add(new TextBlock { Text = amount, FontSize = 16, FontWeight = FontWeights.SemiBold });
                cell.Children.Add(text);
                wrap.Children.Add(cell);
            }
            from.Children.Add(wrap);
        }
        _body.Children.Add(Wrap(from));

        // daily and weekly side by side when there is room
        var daily = Section(t.ChecklistDaily, _list.Daily, false, now, res, runs);
        var weekly = Section(t.ChecklistWeekly, _list.Weekly, true, now, res, runs);
        if (ActualWidth >= 820)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var left = Wrap(daily);
            left.Margin = new Thickness(0, 0, 6, 0);
            left.VerticalAlignment = VerticalAlignment.Top;
            var right = Wrap(weekly);
            right.Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(right, 1);
            grid.Children.Add(left);
            grid.Children.Add(right);
            _body.Children.Add(grid);
        }
        else
        {
            _body.Children.Add(Wrap(daily));
            _body.Children.Add(Wrap(weekly));
        }
        _body.Children.Add(new TextBlock { Text = t.ChecklistFooter, Foreground = Muted, FontSize = 11.5, Margin = new Thickness(4, 10, 4, 0), TextWrapping = TextWrapping.Wrap });
    }

    private StackPanel Section(string title, IReadOnlyList<Checklist.Task> tasks, bool weekly, DateTimeOffset now,
        Dictionary<int, string> res, Dictionary<string, int> runs)
    {
        var t = UiText.Current;
        var counts = tasks.Select(task => Done(task, now, runs)).ToList();
        var done = tasks.Select((task, i) => counts[i] >= task.Max).Count(x => x);
        var left = Checklist.NextReset(weekly, now) - now;
        var when = left.TotalHours >= 24 ? $"{(int)left.TotalDays} {t.DaysShort} {left.Hours} {t.HoursShort}"
            : left.TotalMinutes >= 60 ? $"{(int)left.TotalHours} {t.HoursShort} {left.Minutes} {t.MinutesShort}" : $"{Math.Max(1, (int)left.TotalMinutes)} {t.MinutesShort}";
        var card = Card(title, string.Format(t.ChecklistDoneOf, done, tasks.Count) + "   ·   " + string.Format(t.ChecklistResetsIn, when));
        for (var i = 0; i < tasks.Count; i++) card.Children.Add(Row(tasks[i], counts[i], now, res, runs));
        return card;
    }

    /// <summary>Ticked, or more when the meter saw more runs of that dungeon this week.</summary>
    private int Done(Checklist.Task task, DateTimeOffset now, Dictionary<string, int> runs)
    {
        var ticked = Checklist.Count(Marks.GetValueOrDefault(task.Id), task.Weekly, now);
        var seen = task.Runs is { } d ? runs.GetValueOrDefault(d) : 0;
        return Math.Min(task.Max, Math.Max(ticked, seen));
    }

    private UIElement Row(Checklist.Task task, int count, DateTimeOffset now, Dictionary<int, string> res, Dictionary<string, int> runs)
    {
        var t = UiText.Current;
        var lang = t.Code;
        var full = count >= task.Max;
        var grid = new Grid { Margin = new Thickness(0, 7, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // tick box (one-off task) or − / + (a counter)
        FrameworkElement control;
        if (task.Max == 1)
        {
            var box = new CheckBox { IsChecked = full, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0), Cursor = System.Windows.Input.Cursors.Hand };
            box.Click += (_, _) => Set(task, box.IsChecked == true ? 1 : 0, now);
            control = box;
        }
        else
        {
            var pm = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            pm.Children.Add(Small("−", () => Set(task, Math.Max(0, count - 1), now)));
            pm.Children.Add(Small("+", () => Set(task, Math.Min(task.Max, count + 1), now)));
            control = pm;
        }
        grid.Children.Add(control);

        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = task.Name(lang), FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = full ? Muted : Text, TextWrapping = TextWrapping.Wrap };
        mid.Children.Add(name);
        var tags = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        // what the game says about it
        if (task.Resource != 0 && res.TryGetValue(task.Resource, out var have))
            tags.Children.Add(Tag(string.Format(t.ChecklistGameHas, t.ResourceName(task.Resource), have), Cyan));
        if (task.Runs is { } d && runs.TryGetValue(d, out var seen) && seen > 0)
            tags.Children.Add(Tag(string.Format(t.ChecklistRunsSeen, seen), Cyan));
        if (task.Hint(lang) is { } hint) tags.Children.Add(new TextBlock { Text = hint, Foreground = Muted, FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        if (tags.Children.Count > 0) mid.Children.Add(tags);
        if (task.Max > 1)
        {
            var bar = new Grid { Height = 3, Margin = new Thickness(0, 5, 0, 0) };
            bar.Children.Add(new Border { Background = Track, CornerRadius = new CornerRadius(2) });
            bar.Children.Add(new Border
            {
                Background = full ? Green : Cyan, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left,
                Width = 0, Tag = (double)count / task.Max,
            });
            bar.SizeChanged += (_, e) => ((Border)bar.Children[1]).Width = e.NewSize.Width * (double)((Border)bar.Children[1]).Tag;
            mid.Children.Add(bar);
        }
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);

        var state = new TextBlock
        {
            Text = full ? "✓ " + t.ChecklistDone : task.Max > 1 ? $"{count} / {task.Max}" : "",
            Foreground = full ? Green : Text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
        };
        Grid.SetColumn(state, 2);
        grid.Children.Add(state);
        return grid;
    }

    private void Set(Checklist.Task task, int count, DateTimeOffset now)
    {
        Marks[task.Id] = Checklist.Save(count, task.Weekly, now);
        _meter.Settings.Save();
        _shownKey = "";
        Refresh();
    }

    /// <summary>This character's tickets and energy as the game last sent them (kept per character by the overlay).</summary>
    private Dictionary<int, string> Resources()
    {
        var all = _meter.Settings.Resources;
        if (string.IsNullOrEmpty(Me) || !all.TryGetValue(Me, out var mine)) return new Dictionary<int, string>();
        return mine.ToDictionary(kv => kv.Key, kv => kv.Value is [var c, var b] && b > 0 ? $"{c}+{b}" : kv.Value.FirstOrDefault().ToString());
    }

    /// <summary>This week's runs you were in, by the dungeon's English name.</summary>
    private Dictionary<string, int> RunsThisWeek(DateTimeOffset now)
    {
        var from = Checklist.PeriodStart(true, now).ToUnixTimeMilliseconds();
        return _meter.DungeonRuns()
            .Where(r => r.EnteredMs >= from && r.Members.Any(m => m.IsSelf && (Me.Length == 0 || string.Equals(m.Name, Me, StringComparison.OrdinalIgnoreCase))))
            .GroupBy(r => _dungeonsEn.GetValueOrDefault(r.MapId) ?? r.Zone)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static Dictionary<int, string> LoadDungeonNames()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(MeterService.DataDirectory, "dungeons", "en.json")));
            return doc.RootElement.EnumerateObject()
                .Where(p => int.TryParse(p.Name, out _) && p.Value.TryGetProperty("name", out _))
                .ToDictionary(p => int.Parse(p.Name), p => p.Value.GetProperty("name").GetString() ?? "");
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Dictionary<int, string>();
        }
    }

    private static StackPanel Card(string title, string? note)
    {
        var card = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
        if (note is not null)
        {
            var n = new TextBlock { Text = note, Foreground = Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(n, Dock.Right);
            head.Children.Add(n);
        }
        head.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold });
        card.Children.Add(head);
        return card;
    }

    private static Border Wrap(UIElement child) => new()
    {
        Background = Panel, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
        Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 0, 0, 12), Child = child,
    };

    private static Border Tag(string text, Brush color) => new()
    {
        BorderBrush = color, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 0, 5, 1),
        Margin = new Thickness(0, 0, 6, 0), Child = new TextBlock { Text = text, Foreground = color, FontSize = 11 },
    };

    private static Button Small(string text, Action click)
    {
        var b = new Button
        {
            Content = text, Width = 24, Height = 22, Margin = new Thickness(0, 0, 3, 0), Padding = new Thickness(0),
            Background = Frozen(Color.FromRgb(0x1C, 0x23, 0x33)), Foreground = Text, BorderBrush = Line, Cursor = System.Windows.Input.Cursors.Hand,
        };
        b.Click += (_, _) => click();
        return b;
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
