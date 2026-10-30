using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private static readonly HttpClient Http = CreateHttp();
    private static readonly string CacheDir = System.IO.Path.Combine(AppSettings.AppDataDir, "maps");

    private readonly MeterService _meter;
    private readonly WorldMapView _view = new();
    private readonly ComboBox _pick = new() { Width = 190, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly WrapPanel _chips = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD2, 0xE0)), Margin = new Thickness(10, 5, 10, 6), TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Dictionary<string, ImageSource?> _images = new();
    private static readonly Dictionary<string, ImageSource?> Icons = new(); // game icons by resource name (null: downloading or missing)
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Button _whereButton = new() { Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(10, 3, 10, 3), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private bool _followed;
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
        _view.Icon = IconFor;
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

    /// <summary>A game icon from the game's CDN, kept in %AppData%\1HP-Overlay\maps\icons; null while it downloads.</summary>
    private ImageSource? IconFor(string name)
    {
        if (Icons.TryGetValue(name, out var icon)) return icon;
        Icons[name] = null;
        _ = LoadIconAsync(name);
        return null;
    }

    private async Task LoadIconAsync(string name)
    {
        var file = System.IO.Path.Combine(CacheDir, "icons", name + ".png");
        try
        {
            if (!File.Exists(file))
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
                var bytes = await Http.GetByteArrayAsync(GameResources + name + ".png").ConfigureAwait(true);
                var tmp = $"{file}.{Guid.NewGuid():N}.tmp";
                await File.WriteAllBytesAsync(tmp, bytes).ConfigureAwait(true);
                File.Move(tmp, file, overwrite: true);
            }
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 64;
            bmp.UriSource = new Uri(file);
            bmp.EndInit();
            bmp.Freeze();
            Icons[name] = bmp;
            _view.Redraw();
            BuildChips();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            Log.Info($"Map icon {name} not loaded: {ex.Message}");
        }
    }

    private const string GameResources = "https://assets.playnccdn.com/static-aion2-gamedata/resources/";

    private string Hint() => _map is null ? "" : string.Format(UiText.Current.MapHint, _map.Name, $"{_map.Metres:0}");

    private void ShowMap(string slug)
    {
        var map = _meter.Maps.Find(slug);
        if (map is null) return;
        _map = map;
        _meter.Settings.MapSlug = slug;
        _meter.Settings.Save();
        BuildChips();
        _view.SetMap(map, _images.GetValueOrDefault(slug));
        ApplyLayers();
        _status.Text = Hint();
        if (!_images.ContainsKey(slug)) _ = LoadImageAsync(map);
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
                        IconFor(WorldMaps.LayerIcon(key)) is { } icon
                            ? new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }
                            : new Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(WorldMapView.LayerColor(key)), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center },
                        label,
                    },
                },
            };
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

    private void ApplyLayers()
    {
        var have = _meter.Settings.PetsDone;
        var ids = _meter.Pets.All.GroupBy(s => s.Pet).ToDictionary(g => g.Key, g => g.First().PetId);
        _view.SetLayers(_meter.Settings.MapLayers.ToHashSet(),
            place => ids.TryGetValue(place.Name, out var id) && have.Contains(id)); // pets you have: small and grey
    }

    private async Task LoadImageAsync(WorldMaps.Map map)
    {
        _images[map.Slug] = null;
        var file = System.IO.Path.Combine(CacheDir, map.Slug + ".webp");
        try
        {
            if (!File.Exists(file))
            {
                _status.Text = UiText.Current.MapLoading;
                await DownloadAsync(map, file);
            }
            var bmp = Decode(file);
            _images[map.Slug] = bmp;
            if (_map?.Slug == map.Slug)
            {
                _view.SetImage(bmp);
                _status.Text = Hint();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            Log.Info($"Map picture {map.Slug} not loaded: {ex.Message}");
            _images.Remove(map.Slug); // try again next time
            if (_map?.Slug == map.Slug)
                _status.Text = ex is NotSupportedException or FileFormatException ? UiText.Current.MapNoCodec : UiText.Current.MapNoPicture;
        }
    }

    private static async Task DownloadAsync(WorldMaps.Map map, string file)
    {
        Directory.CreateDirectory(CacheDir);
        var bytes = await Http.GetByteArrayAsync(map.ImageUrl).ConfigureAwait(false);
        var tmp = $"{file}.{Guid.NewGuid():N}.tmp"; // a second meter may fetch the same picture
        await File.WriteAllBytesAsync(tmp, bytes).ConfigureAwait(false);
        File.Move(tmp, file, overwrite: true);
    }

    /// <summary>WebP through the Windows imaging codec (built into Windows 11; "WebP Image Extensions" on 10).</summary>
    private static BitmapImage Decode(string file)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(file);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    /// <summary>The preview renderer: the picture of the map shown, downloaded and drawn before the window is captured.</summary>
    public void LoadPictureNow()
    {
        if (_map is null) return;
        var file = System.IO.Path.Combine(CacheDir, _map.Slug + ".webp");
        try
        {
            if (!File.Exists(file)) DownloadAsync(_map, file).GetAwaiter().GetResult();
            _images[_map.Slug] = Decode(file);
            _view.SetImage(_images[_map.Slug]);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or NotSupportedException or FileFormatException)
        {
            Log.Info($"Map picture {_map.Slug} not loaded: {ex.Message}");
        }
        try
        {
            // and the icons of the layers shown, so the preview has them all
            foreach (var key in _meter.Settings.MapLayers)
                if (_map.Layers.TryGetValue(key, out var places))
                    foreach (var name in places.Select(p => WorldMaps.IconOf(key, p)).Distinct())
                    {
                        var icon = System.IO.Path.Combine(CacheDir, "icons", name + ".png");
                        if (!File.Exists(icon))
                        {
                            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(icon)!);
                            File.WriteAllBytes(icon, Http.GetByteArrayAsync(GameResources + name + ".png").GetAwaiter().GetResult());
                        }
                        Icons[name] = new BitmapImage(new Uri(icon));
                    }
            BuildChips();
            _view.Redraw();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or NotSupportedException or FileFormatException)
        {
            Log.Info($"Map icons not loaded: {ex.Message}");
        }
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("1HP-Overlay (world map, credited to Aion2Data)");
        return http;
    }
}
