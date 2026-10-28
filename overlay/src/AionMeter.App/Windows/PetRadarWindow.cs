using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AionMeter.App.Controls;
using AionMeter.App.Services;

namespace AionMeter.App.Windows;

/// <summary>
/// 1 HP: the pet radar as its own small window (tray, Ctrl+Shift+P): soul monsters as dots around you. The mouse wheel
/// changes the range, right click turns the radar a quarter (to line it up with the game's map), drag moves it.
/// </summary>
public sealed class PetRadarWindow : Window
{
    private readonly MeterService _meter;
    private readonly RadarView _view = new();
    private readonly TextBlock _caption = new() { FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xB4, 0xBC, 0xCB)), Margin = new Thickness(10, 6, 10, 0) };
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public PetRadarWindow(MeterService meter)
    {
        _meter = meter;
        Title = "1 HP Pet radar";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 260;
        Height = 290;
        Left = 40;
        Top = 300;
        FontFamily = new FontFamily("Segoe UI");
        WindowMemory.Track(this, "petradar");

        _view.RangeMeters = Math.Clamp(meter.Settings.PetRadarRange, 25, 400);
        _view.Rotation = meter.Settings.PetRadarRotation;
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.Children.Add(_caption);
        Grid.SetRow(_view, 1);
        _view.Margin = new Thickness(6);
        grid.Children.Add(_view);
        Content = new Border
        {
            CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x0E, 0x13, 0x20)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0x6E, 0x8B, 0xFF)), BorderThickness = new Thickness(1), Child = grid,
            ToolTip = UiText.Current.PetRadarWindowTip,
        };

        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        MouseWheel += (_, e) =>
        {
            var range = Math.Clamp(_view.RangeMeters * (e.Delta > 0 ? 0.8 : 1.25), 25, 400);
            _view.RangeMeters = meter.Settings.PetRadarRange = Math.Round(range);
            meter.Settings.Save();
            Refresh();
        };
        MouseRightButtonUp += (_, _) =>
        {
            _view.Rotation = meter.Settings.PetRadarRotation = (meter.Settings.PetRadarRotation + 1) % 4;
            meter.Settings.Save();
            Refresh();
        };
        SourceInitialized += (_, _) =>
            NativeMethods.SetExStyle(new WindowInteropHelper(this).Handle, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        _timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); _timer.Start(); } else _timer.Stop(); };
    }

    public void Refresh()
    {
        var t = UiText.Current;
        var pic = _meter.Pets.Map(_meter.Tracker.LastEventMs);
        var have = _meter.Settings.PetsDone;
        _view.SetData(pic, have);
        var missing = pic.Dots.Count(d => !have.Contains(d.Soul.PetId));
        _caption.Text = $"{t.PetRadarTitle} · " + string.Format(t.PetRadarCount, missing, pic.Dots.Count - missing) +
                        (pic.Center is null && pic.Dots.Count > 0 ? " · " + t.PetRadarNoCenter : "");
    }
}
