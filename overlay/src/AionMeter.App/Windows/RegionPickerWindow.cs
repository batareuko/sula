using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AionMeter.App.Services;

namespace AionMeter.App.Windows;

/// <summary>
/// 1 HP: F9 opens a full-screen picker — drag with the mouse to choose the area the cube tracker screenshots.
/// Esc / right click cancels; the chosen rectangle is returned in physical pixels.
/// </summary>
public sealed class RegionPickerWindow : Window
{
    private readonly PickerView _view = new();
    private readonly Action<ScreenRegion?> _done;
    private Point _start;
    private bool _dragging;
    private bool _finished;

    public RegionPickerWindow(Action<ScreenRegion?> done)
    {
        _done = done;
        Title = "1 HP pick area";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Content = _view;
        Cursor = Cursors.Cross;

        MouseLeftButtonDown += (_, e) =>
        {
            _start = e.GetPosition(this);
            _dragging = true;
            _view.SetSelection(new Rect());
        };
        MouseMove += (_, e) =>
        {
            if (_dragging) _view.SetSelection(Normalize(_start, e.GetPosition(this)));
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_dragging) return;
            _dragging = false;
            var r = Normalize(_start, e.GetPosition(this));
            if (r.Width >= 8 && r.Height >= 8) Finish(r);
            else _view.SetSelection(new Rect());
        };
        MouseRightButtonDown += (_, _) => Cancel();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Cancel();
        };
        Loaded += (_, _) => Focus();
    }

    private void Finish(Rect r)
    {
        if (_finished) return;
        _finished = true;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var v = ScreenCapture.VirtualScreen();
        var region = new ScreenRegion(
            (int)Math.Round(v.Left + r.X * dpi),
            (int)Math.Round(v.Top + r.Y * dpi),
            (int)Math.Round(r.Width * dpi),
            (int)Math.Round(r.Height * dpi));
        Close();
        _done(region);
    }

    private void Cancel()
    {
        if (_finished) return;
        _finished = true;
        Close();
        _done(null);
    }

    private static Rect Normalize(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    private sealed class PickerView : FrameworkElement
    {
        private Rect _selection;

        public void SetSelection(Rect r)
        {
            _selection = r;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            var w = ActualWidth;
            var h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00)), null, new Rect(0, 0, w, h));
            if (_selection.Width > 0 && _selection.Height > 0)
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x2E, 0x39, 0xFF, 0x14)), null, _selection);
                var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x39, 0xFF, 0x14)), 2);
                pen.Freeze();
                dc.DrawRectangle(null, pen, _selection);
            }
            var text = new FormattedText(UiText.Current.CubeDragHint, CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(24, 24));
        }
    }
}
