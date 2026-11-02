using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AionMeter.App.Controls;
using AionMeter.App.Services;
using AionMeter.Core;
using AionMeter.Core.OneHp;

namespace AionMeter.App.Windows;

/// <summary>
/// 1 HP: the PvP death note (tray 💀, Ctrl+Shift+K) — the players who killed you, the most kills first: class, server,
/// how many times, when last, where, their combat power and item level (the meter's own sightings and the public profile).
/// "2+" shows only the ones who did it again; the guild's note is on the site.
/// </summary>
public sealed class DeathNoteWindow : Window
{
    private readonly MeterService _meter;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _summary = new() { Foreground = Muted, FontSize = 12, Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _all = new() { Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };

    private static readonly Brush Muted = Frozen(Color.FromRgb(0x9A, 0xA4, 0xB8));
    private static readonly Brush Text = Frozen(Color.FromRgb(0xE4, 0xEA, 0xF4));
    private static readonly Brush Red = Frozen(Color.FromRgb(0xFF, 0x5A, 0x5A));
    private static readonly Brush Panel = Frozen(Color.FromRgb(0x14, 0x1A, 0x29));
    private static readonly Brush Line = Frozen(Color.FromArgb(0x30, 0x6E, 0x8B, 0xFF));

    public DeathNoteWindow(MeterService meter)
    {
        _meter = meter;
        var t = UiText.Current;
        Title = "1 HP — " + t.DeathNoteTitle;
        Width = 720;
        Height = 640;
        MinWidth = 480;
        MinHeight = 320;
        Background = Frozen(Color.FromRgb(0x0E, 0x13, 0x20));
        FontFamily = new FontFamily("Segoe UI");
        Foreground = Text;
        WindowMemory.Track(this, "deathnote");

        _all.Content = new TextBlock { Text = t.DeathNoteShowAll, Foreground = Muted };
        _all.Click += (_, _) => Refresh();
        var head = new DockPanel { Margin = new Thickness(16, 12, 16, 4) };
        DockPanel.SetDock(_all, Dock.Right);
        head.Children.Add(_all);
        head.Children.Add(new TextBlock { Text = "💀 " + t.DeathNoteTitle, FontSize = 20, FontWeight = FontWeights.SemiBold });
        var body = new StackPanel { Margin = new Thickness(16, 4, 16, 16) };
        body.Children.Add(_summary);
        body.Children.Add(_rows);
        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = body });
        Content = root;

        meter.DeathNote.Changed += OnChanged;
        Closed += (_, _) => meter.DeathNote.Changed -= OnChanged;
        _timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); _timer.Start(); } else _timer.Stop(); };
    }

    private void OnChanged() => Dispatcher.BeginInvoke(Refresh);

    public void Refresh()
    {
        var t = UiText.Current;
        var note = _meter.DeathNote;
        var list = note.Nemeses(_all.IsChecked == true ? 1 : 2);
        var deaths = note.All.Count;
        _summary.Text = deaths == 0 ? t.DeathNoteEmpty : string.Format(t.DeathNoteSummary, deaths, note.Nemeses(1).Count, note.Nemeses(2).Count);
        _rows.Children.Clear();
        foreach (var n in list)
        {
            // the public profile's numbers, when the lookup has them by now
            if (_meter.Cloud.GearFor(n.Name, n.Server) is { } g && (g.CombatPower != n.Power || g.ItemLevel != n.ItemLevel))
                note.Update(n.Name, n.Server, n.Class, g.CombatPower, g.ItemLevel);
            _rows.Children.Add(Row(n));
        }
        if (list.Count == 0 && deaths > 0)
            _rows.Children.Add(new TextBlock { Text = t.DeathNoteNoRepeat, Foreground = Muted, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
    }

    private UIElement Row(DeathNote.Nemesis n)
    {
        var t = UiText.Current;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new ClassGlyph { Class = n.Class, Width = 30, Height = 30, VerticalAlignment = VerticalAlignment.Center });

        var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold };
        name.Inlines.Add(new System.Windows.Documents.Run(n.Name));
        var server = _meter.Data.ServerName(n.Server);
        if (server.Length > 0) name.Inlines.Add(new System.Windows.Documents.Run("  · " + server) { Foreground = Muted, FontWeight = FontWeights.Normal, FontSize = 12.5 });
        mid.Children.Add(name);
        var facts = new List<string> { t.ClassName(n.Class) };
        if (n.Power > 0) facts.Add("⚡ " + Format.Power(n.Power));
        if (n.ItemLevel > 0) facts.Add(string.Format(t.GearScoreShort, n.ItemLevel));
        facts.Add(string.Format(t.DeathNoteLast, n.Last.ToLocalTime().ToString("dd.MM HH:mm")));
        mid.Children.Add(new TextBlock { Text = string.Join(" · ", facts), Foreground = Muted, FontSize = 12 });
        var where = n.Zones.Count > 0 ? string.Format(t.DeathNoteWhere, string.Join(", ", n.Zones.Take(3))) : "";
        var who = n.Victims.Count > 1 ? string.Format(t.DeathNoteVictims, string.Join(", ", n.Victims)) : "";
        if (where.Length + who.Length > 0)
            mid.Children.Add(new TextBlock { Text = string.Join("   ", new[] { where, who }.Where(x => x.Length > 0)), Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(mid, 1);
        grid.Children.Add(mid);

        var kills = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        kills.Children.Add(new TextBlock { Text = "×" + n.Kills, Foreground = n.Kills >= 2 ? Red : Text, FontSize = 22, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Right });
        kills.Children.Add(new TextBlock { Text = t.DeathNoteKills, Foreground = Muted, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right });
        Grid.SetColumn(kills, 2);
        grid.Children.Add(kills);
        return new Border
        {
            Background = Panel, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6), Child = grid,
        };
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
