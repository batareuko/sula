using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using AionMeter.App.Controls;
using AionMeter.App.Services;
using AionMeter.Core.OneHp;

namespace AionMeter.App.Windows;

/// <summary>
/// 1 HP: the world map (tray, Ctrl+Shift+M) — Verteron, Altgard, Eltnen, Morheim and Reshanta with field bosses, dungeon
/// entrances, rifts, Kibelisks, gathering, pet-soul monsters and Hidden Cube spots. Places and pictures: Aion2Data; the
/// picture of a map is downloaded the first time it is opened and kept in %AppData%\1HP-Overlay\maps.
/// </summary>
public sealed class MapWindow : Window
{

    private readonly MeterService _meter;
    private readonly WorldMapView _view = new();
    private readonly ComboBox _pick = new() { Width = 190, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel _chips = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD2, 0xE0)), Margin = new Thickness(10, 5, 10, 6), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Button _whereButton = new() { Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private bool _followed;
    private readonly Button _clearPet = new() { Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private WorldMaps.Map? _map;

    public MapWindow(MeterService meter)
    {
        _meter = meter;
        var t = UiText.Current;
        Title = "1 HP — " + t.MapTitle;
        Width = 900;
        Height = 820;
        MinWidth = 520;
        MinHeight = 420;
        Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x13, 0x20));
        FontFamily = new FontFamily("Segoe UI");
        WindowMemory.Track(this, "map");

        // the one pet layer of 1.20–1.23 is now four, one per family
        if (meter.Settings.MapLayers.Remove("pets"))
            meter.Settings.MapLayers.AddRange(["pets-fera", "pets-natura", "pets-varian", "pets-cogni"]);
        foreach (var m in meter.Maps.All) _pick.Items.Add(new ComboBoxItem { Content = $"{m.Name} · {m.Faction}", Tag = m.Slug });
        _pick.SelectionChanged += (_, _) =>
        {
            if (_pick.SelectedItem is ComboBoxItem { Tag: string slug }) ShowMap(slug);
        };

        var top = new DockPanel { Margin = new Thickness(10, 8, 10, 6) };
        DockPanel.SetDock(_pick, Dock.Left);
        top.Children.Add(_pick);
        _whereButton.Content = t.MapWhereAmI;
        _whereButton.Click += (_, _) => FollowYou(centre: true);
        DockPanel.SetDock(_whereButton, Dock.Left);
        top.Children.Add(_whereButton);
        _clearPet.Content = t.MapPetAll;
        _clearPet.Click += (_, _) => ShowPet("", null);
        DockPanel.SetDock(_clearPet, Dock.Left);
        top.Children.Add(_clearPet);
        var mini = new Button { Content = t.MiniMapButton, Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center, ToolTip = t.MiniMapTray };
        mini.Click += (_, _) => AppHost.Current.ToggleMiniMap();
        DockPanel.SetDock(mini, Dock.Left);
        top.Children.Add(mini);
        top.Children.Add(_chips);

        var credit = new TextBlock { Margin = new Thickness(10, 5, 10, 6), HorizontalAlignment = HorizontalAlignment.Right };
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(t.MapCredit)) { Foreground = new SolidColorBrush(Color.FromRgb(0x8F, 0xB4, 0xFF)) };
        link.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://aion2data.com/map") { UseShellExecute = true });
        credit.Inlines.Add(link);
        var bottom = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(0x14, 0x1A, 0x29)) };
        DockPanel.SetDock(credit, Dock.Right);
        bottom.Children.Add(credit);
        bottom.Children.Add(_status);

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(top);
        Grid.SetRow(_view, 1);
        grid.Children.Add(_view);
        Grid.SetRow(bottom, 2);
        grid.Children.Add(bottom);
        Content = grid;

        _view.HoverChanged += text => _status.Text = text ?? Hint();
        _view.Icon = MapAssets.Icon;
        MapAssets.Changed += OnAssets;
        Closed += (_, _) => MapAssets.Changed -= OnAssets;
        _timer.Tick += (_, _) => FollowYou(centre: false);
        IsVisibleChanged += (_, _) => { if (IsVisible) _timer.Start(); else _timer.Stop(); };
        _view.ToolTip = t.MapTip;
        ToolTipService.SetInitialShowDelay(_view, 1500);

        if (meter.Maps.All.Count == 0)
        {
            _status.Text = t.MapNoData;
            return;
        }
        // the map you are on (when the meter knows it), else the one shown last
        var here = meter.Pets.Where(meter.Tracker.LastEventMs) is { } w ? meter.Maps.ForGameMap(w.MapId) : null;
        var slug = here?.Slug ?? meter.Maps.Find(meter.Settings.MapSlug)?.Slug ?? meter.Maps.All[0].Slug;
        _pick.SelectedItem = _pick.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == slug);
    }

    /// <summary>
    /// 1 HP: "you are here" — your position from the meter (your own moves, the monster you hit, what came into view after a
    /// teleport), on the maps whose game coordinates are calibrated. <paramref name="centre"/>: switch to your map and centre on you.
    /// </summary>
    private void FollowYou(bool centre)
    {
        var w = _meter.Pets.Where(_meter.Tracker.LastEventMs);
        var map = w is null ? null : _meter.Maps.ForGameMap(w.MapId);
        _whereButton.Visibility = map is null ? Visibility.Collapsed : Visibility.Visible;
        if (map is null || w is null)
        {
            _view.SetYou(null, false);
            return;
        }
        if (centre && _map?.Slug != map.Slug)
            _pick.SelectedItem = _pick.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == map.Slug);
        _view.SetYou(_map?.Slug == map.Slug ? map.ToMap(w.X, w.Y) : null, w.Exact);
        if (centre || !_followed)
        {
            _followed = true;
            if (_map?.Slug == map.Slug) _view.CentreOnYou();
        }
    }


    private string Hint() => _map is null ? "" : string.Format(UiText.Current.MapHint, _map.Name, $"{_map.Metres:0}");

    private void ShowMap(string slug)
    {
        var map = _meter.Maps.Find(slug);
        if (map is null) return;
        _map = map;
        _meter.Settings.MapSlug = slug;
        _meter.Settings.Save();
        BuildChips();
        _view.SetMap(map, MapAssets.Picture(map));
        if (_only is not null) ShowPet("", null); // another map: all pets again
        ApplyLayers();
        _status.Text = MapAssets.Downloading(slug) ? UiText.Current.MapLoading : Hint();
    }

    private void BuildChips()
    {
        if (_map is null) return;
        _chips.Children.Clear();
        var t = UiText.Current;
        foreach (var key in WorldMaps.LayerOrder)
        {
            if (!_map.Layers.TryGetValue(key, out var places)) continue;
            var count = places.Count;
            var on = _meter.Settings.MapLayers.Contains(key);
            var label = new TextBlock { Text = $"{t.MapLayer(key)} {count}", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            var chip = new Border
            {
                Margin = new Thickness(0, 2, 6, 2), Padding = new Thickness(8, 3, 9, 3), CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1), Cursor = System.Windows.Input.Cursors.Hand,
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        !key.StartsWith("pets", StringComparison.Ordinal) && MapAssets.Icon(WorldMaps.LayerIcon(key)) is { } icon
                            ? new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }
                            : new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(WorldMapView.LayerColor(key)), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center },
                        label,
                    },
                },
            };
            // pet souls: ▾ lists the family's pets on this map, to show one of them
            if (key.StartsWith("pets", StringComparison.Ordinal) || key == "gather")
            {
                var arrow = new TextBlock
                {
                    Text = "  ▾", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.White,
                    ToolTip = key == "gather" ? t.MapGatherList : t.MapPetList,
                };
                arrow.MouseLeftButtonUp += (_, e) =>
                {
                    e.Handled = true; // the arrow opens the list; the rest of the chip turns the layer on / off
                    PetMenu(key, places).IsOpen = true;
                };
                ((StackPanel)chip.Child).Children.Add(arrow);
            }
            void Paint()
            {
                var c = WorldMapView.LayerColor(key);
                chip.Background = new SolidColorBrush(on ? Color.FromArgb(0x40, c.R, c.G, c.B) : Color.FromRgb(0x1C, 0x23, 0x33));
                chip.BorderBrush = new SolidColorBrush(on ? c : Color.FromArgb(0x50, 0x6E, 0x8B, 0xFF));
                label.Foreground = new SolidColorBrush(on ? Colors.White : Color.FromRgb(0x8A, 0x94, 0xA6));
            }
            Paint();
            chip.MouseLeftButtonUp += (_, _) =>
            {
                on = !on;
                var layers = _meter.Settings.MapLayers;
                if (on && !layers.Contains(key)) layers.Add(key);
                if (!on) layers.Remove(key);
                _meter.Settings.Save();
                Paint();
                ApplyLayers();
            };
            _chips.Children.Add(chip);
        }
    }

    private WorldMaps.Place? _only;

    /// <summary>
    /// The pets of one family on this map by name (the soul monster, how many spots, ✓ for the ones you have), or the
    /// gathering nodes by what they give (with its icon).
    /// </summary>
    private ContextMenu PetMenu(string key, IReadOnlyList<WorldMaps.Place> places)
    {
        var t = UiText.Current;
        var have = _meter.Settings.PetsDone;
        var ids = _meter.Pets.All.GroupBy(s => s.Pet).ToDictionary(g => g.Key, g => g.First().PetId);
        var menu = new ContextMenu { MaxHeight = 520 };
        foreach (var place in places.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var owned = ids.TryGetValue(place.Name, out var id) && have.Contains(id);
            var item = new MenuItem
            {
                Header = $"{(owned ? "✓ " : "")}{place.Name}  ·  {place.Sub}  ·  {place.Points.Count}",
                IsChecked = ReferenceEquals(place, _only),
                Foreground = owned ? Brushes.Gray : Brushes.Black,
                // a gathering node shows what it gives
                Icon = key == "gather" && MapAssets.Icon(WorldMaps.IconOf(key, place)) is { } img ? new Image { Source = img, Width = 20, Height = 20 } : null,
            };
            item.Click += (_, _) => ShowPet(key, place);
            menu.Items.Add(item);
        }
        return menu;
    }

    private void ShowPet(string key, WorldMaps.Place? place)
    {
        _only = place;
        if (place is not null && !_meter.Settings.MapLayers.Contains(key))
        {
            _meter.Settings.MapLayers.Add(key); // the family's layer on, so the pet shows
            _meter.Settings.Save();
            ApplyLayers();
            BuildChips();
        }
        _view.ShowOnly(place, key);
        _clearPet.Visibility = place is null ? Visibility.Collapsed : Visibility.Visible;
        _status.Text = place is null ? Hint() : string.Format(UiText.Current.MapPetShown, place.Name, place.Sub, place.Points.Count);
    }

    /// <summary>The preview renderer: the first pet of a family shown alone.</summary>
    public void ShowPetForPreview(string key)
    {
        if (_map?.Layers.GetValueOrDefault(key) is { Count: > 0 } places)
        {
            Measure(new Size(Width, Height));
            Arrange(new Rect(0, 0, Width, Height));
            UpdateLayout();
            ShowPet(key, places.OrderByDescending(p => p.Points.Count).First());
        }
    }

    private void ApplyLayers()
    {
        var have = _meter.Settings.PetsDone;
        var ids = _meter.Pets.All.GroupBy(s => s.Pet).ToDictionary(g => g.Key, g => g.First().PetId);
        _view.SetLayers(_meter.Settings.MapLayers.ToHashSet(),
            place => ids.TryGetValue(place.Name, out var id) && have.Contains(id)); // pets you have: small and grey
    }

    /// <summary>The preview renderer: the picture of the map shown and the icons, drawn before the window is captured.</summary>
    public void LoadPictureNow()
    {
        if (_map is null) return;
        MapAssets.LoadNow(_map, _meter.Settings.MapLayers);
        _view.SetImage(MapAssets.Picture(_map));
        BuildChips();
        _view.Redraw();
    }

    private void OnAssets()
    {
        if (_map is null) return;
        _view.SetImage(MapAssets.Picture(_map));
        _view.Redraw();
        BuildChips();
        if (MapAssets.Problem(_map.Slug) is { } why) _status.Text = why == "codec" ? UiText.Current.MapNoCodec : UiText.Current.MapNoPicture;
        else if (_status.Text == UiText.Current.MapLoading) _status.Text = Hint();
    }
}
