using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AionMeter.App.Controls;
using AionMeter.App.Services;

namespace AionMeter.App.Windows;

/// <summary>
/// 1 HP: the mini-map over the game (tray, Ctrl+Shift+N; or the button in the world map) — a small piece of the world map
/// with you in the middle and the layers of the big map. Wheel — zoom, drag — move, right click — size, opacity, the big
/// map. Shown on the maps whose game coordinates are calibrated (Verteron for now); elsewhere it says it does not know where you are.
/// </summary>
public sealed class MiniMapWindow : Window
{
    private readonly MeterService _meter;
    private readonly WorldMapView _view = new() { Compact = true };
    private readonly TextBlock _note = new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0xC9, 0xD2, 0xE0)), FontSize = 11, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14), IsHitTestVisible = false,
    };
    private readonly TextBlock _place = new()
    {
        Foreground = Brushes.White, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false,
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.9 },
    };
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private string? _slug;

    public MiniMapWindow(MeterService meter)
    {
        _meter = meter;
        Title = "1 HP Mini-map";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Left = 40;
        Top = 120;
        FontFamily = new FontFamily("Segoe UI");
        WindowMemory.Track(this, "minimap");

        var grid = new Grid();
        grid.Children.Add(_view);
        grid.Children.Add(_note);
        grid.Children.Add(_place);
        Content = new Border
        {
            CornerRadius = new CornerRadius(14), ClipToBounds = true, BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x90, 0xC9, 0xA4, 0x5C)), Background = new SolidColorBrush(Color.FromRgb(0x0A, 0x14, 0x1A)),
            Child = grid, ToolTip = UiText.Current.MiniMapTip,
        };
        ((Border)Content).Loaded += (_, _) => Clip();
        ApplySize();

        _view.Icon = MapAssets.Icon;
        MapAssets.Changed += OnAssets;
        Closed += (_, _) => MapAssets.Changed -= OnAssets;
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        MouseWheel += (_, e) =>
        {
            var s = _meter.Settings;
            s.MiniMapZoom = Math.Clamp(s.MiniMapZoom * (e.Delta > 0 ? 1.25 : 0.8), 2, 40);
            s.Save();
            Refresh();
        };
        MouseRightButtonUp += (_, _) => Menu().IsOpen = true;
        SourceInitialized += (_, _) =>
            NativeMethods.SetExStyle(new WindowInteropHelper(this).Handle, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        _timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); _timer.Start(); } else _timer.Stop(); };
    }

    /// <summary>Size (px) and opacity from the settings; the interface scale grows it with the rest.</summary>
    public void ApplySize()
    {
        var s = _meter.Settings;
        Width = Height = Math.Clamp(s.MiniMapSize, 140, 480) * Math.Clamp(s.UiScale, 75, 200) / 100.0;
        Opacity = Math.Clamp(s.MiniMapOpacity, 0.3, 1);
        Clip();
    }

    private void Clip()
    {
        if (Content is Border b) b.Clip = new RectangleGeometry(new Rect(0, 0, Width, Height), 14, 14);
    }

    private void OnAssets()
    {
        if (_slug is { } slug && _meter.Maps.Find(slug) is { } map) _view.SetImage(MapAssets.Picture(map));
        _view.Redraw();
    }

    public void Refresh()
    {
        var t = UiText.Current;
        var w = _meter.Pets.Where(_meter.Tracker.LastEventMs);
        var map = w is null ? null : _meter.Maps.ForGameMap(w.MapId);
        if (map is null || w is null)
        {
            _note.Text = t.MiniMapUnknown;
            _note.Visibility = Visibility.Visible;
            _place.Text = "";
            _view.SetYou(null, false);
            return;
        }
        _note.Visibility = Visibility.Collapsed;
        if (_slug != map.Slug)
        {
            _slug = map.Slug;
            _view.SetMap(map, MapAssets.Picture(map));
        }
        // the layers of the big map; pets you have pale
        var have = _meter.Settings.PetsDone;
        var ids = _meter.Pets.All.GroupBy(x => x.Pet).ToDictionary(g => g.Key, g => g.First().PetId);
        _view.SetLayers(_meter.Settings.MapLayers.ToHashSet(), place => ids.TryGetValue(place.Name, out var id) && have.Contains(id));
        if (map.ToMap(w.X, w.Y) is { } at)
        {
            _view.SetYou(at, w.Exact);
            _view.Follow(at, _meter.Settings.MiniMapZoom);
            // the nearest named area under you
            var area = map.Areas.MinBy(a => (a.X - at.X) * (a.X - at.X) + (a.Y - at.Y) * (a.Y - at.Y));
            _place.Text = area is null ? map.Name : area.Name;
        }
    }

    private ContextMenu Menu()
    {
        var t = UiText.Current;
        var s = _meter.Settings;
        var menu = new ContextMenu { PlacementTarget = this };
        MenuItem Item(string text, bool on, Action act)
        {
            var i = new MenuItem { Header = text, IsCheckable = true, IsChecked = on };
            i.Click += (_, _) => { act(); s.Save(); ApplySize(); Refresh(); };
            return i;
        }
        var size = new MenuItem { Header = t.MiniMapSize };
        foreach (var (name, px) in new[] { (t.MiniMapSmall, 180), (t.MiniMapMedium, 240), (t.MiniMapLarge, 320), (t.MiniMapHuge, 420) })
            size.Items.Add(Item(name, s.MiniMapSize == px, () => s.MiniMapSize = px));
        var opacity = new MenuItem { Header = t.MiniMapOpacity };
        foreach (var o in new[] { 1.0, 0.85, 0.7, 0.5 })
            opacity.Items.Add(Item($"{o * 100:0} %", Math.Abs(s.MiniMapOpacity - o) < 0.01, () => s.MiniMapOpacity = o));
        menu.Items.Add(size);
        menu.Items.Add(opacity);
        menu.Items.Add(new Separator());
        var big = new MenuItem { Header = t.MapTray };
        big.Click += (_, _) => AppHost.Current.ShowMap();
        menu.Items.Add(big);
        var hide = new MenuItem { Header = t.MiniMapHide };
        hide.Click += (_, _) => AppHost.Current.ToggleMiniMap();
        menu.Items.Add(hide);
        return menu;
    }
}
