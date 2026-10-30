using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AionMeter.App.Controls;
using AionMeter.App.Services;
using AionMeter.Core.Combat;

namespace AionMeter.App.Windows;

/// <summary>
/// 1 HP: the party window (tray 👥, Ctrl+Shift+G) — you and your party: class, name, an HP bar with its percent, the buffs
/// and debuffs on each one, and the cooldowns under each row (the skill's icon and the time left; yours all, theirs the long ones). The stream page
/// <c>/party</c> shows the same window (OBS browser source). Drag moves it.
/// </summary>
public sealed class PartyWindow : Window
{
    private readonly MeterService _meter;
    private readonly StackPanel _rows = new() { Margin = new Thickness(10, 6, 10, 10) };
    private readonly System.Windows.Threading.DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _encoding;

    private static readonly Brush Muted = Frozen(Color.FromRgb(0x9A, 0xA4, 0xB8));
    private static readonly Brush Gold = Frozen(Color.FromRgb(0xFF, 0xD3, 0x4D));
    private static readonly Brush Track = Frozen(Color.FromArgb(0xB0, 0x1C, 0x23, 0x33));
    private static readonly Brush Dead = Frozen(Color.FromRgb(0xFF, 0x5A, 0x5A));

    public PartyWindow(MeterService meter)
    {
        _meter = meter;
        Title = "1 HP Party";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        Left = 40;
        Top = 640;
        FontFamily = new FontFamily("Segoe UI");
        WindowMemory.Track(this, "party");
        Content = new Border
        {
            CornerRadius = new CornerRadius(12), Background = Frozen(Color.FromArgb(0xCC, 0x0E, 0x13, 0x20)),
            BorderBrush = Frozen(Color.FromArgb(0x50, 0x6E, 0x8B, 0xFF)), BorderThickness = new Thickness(1), Child = _rows,
            ToolTip = UiText.Current.PartyWindowTip,
        };
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        SourceInitialized += (_, _) =>
            NativeMethods.SetExStyle(new WindowInteropHelper(this).Handle, NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        _timer.Tick += (_, _) =>
        {
            Refresh();
            if (_meter.Stream.WantsWindowFrame("party")) CaptureStreamFrame();
        };
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); _timer.Start(); } else _timer.Stop(); };
    }

    /// <summary>1 HP: interface scale — the window grows with the rest of the overlay.</summary>
    public void ApplyScale(int percent)
    {
        var s = percent / 100.0;
        if (Content is FrameworkElement root) root.LayoutTransform = s == 1 ? Transform.Identity : new ScaleTransform(s, s);
        Width = 340 * s;
    }

    public void Refresh()
    {
        var t = UiText.Current;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var members = _meter.Tracker.PartyFrame(now);
        _rows.Children.Clear();
        _rows.Children.Add(new TextBlock { Text = t.PartyWindowTitle, Foreground = Muted, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 4) });
        if (members.Count == 0)
        {
            _rows.Children.Add(new TextBlock { Text = t.PartyWindowEmpty, Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 2, 2, 4) });
            return;
        }
        foreach (var m in members) _rows.Children.Add(Row(m, now));
    }

    private UIElement Row(PartyFrameMember m, long now)
    {
        var t = UiText.Current;
        var stale = m.HpAtMs > 0 && now - m.HpAtMs > 60_000; // the game has not sent their HP for a while (far away)
        var box = new StackPanel { Margin = new Thickness(0, 3, 0, 3), Opacity = stale || m.ActorId is null ? 0.55 : 1 };
        var line = new Grid();
        line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        line.ColumnDefinitions.Add(new ColumnDefinition());
        line.Children.Add(new ClassGlyph { Class = m.Class, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left });

        var mid = new StackPanel();
        var head = new DockPanel();
        var frac = m.HpFraction;
        var dead = m.Hp == 0;
        var pct = new TextBlock
        {
            Text = dead ? "†" : frac >= 0 ? $"{frac * 100:0}%" : stale || m.ActorId is null ? t.PartyWindowFar : "",
            Foreground = dead ? Dead : Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold,
        };
        DockPanel.SetDock(pct, Dock.Right);
        head.Children.Add(pct);
        head.Children.Add(new TextBlock
        {
            Text = m.Name, Foreground = m.IsSelf ? Gold : Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        mid.Children.Add(head);

        // HP bar in the class colour; red when low
        var barColor = dead ? Colors.Transparent : frac is >= 0 and < 0.3 ? Color.FromRgb(0xFF, 0x5A, 0x5A) : ClassVisuals.BarColorOf(m.Class);
        var hp = new Grid { Height = 7, Margin = new Thickness(0, 2, 0, 0) };
        hp.Children.Add(new Border { Background = Track, CornerRadius = new CornerRadius(3) });
        var fill = new Border { Background = new SolidColorBrush(barColor), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
        hp.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * Math.Max(0, frac);
        hp.Children.Add(fill);
        mid.Children.Add(hp);

        // buffs and debuffs on them: debuffs first, then the ones running out soonest
        var buffs = m.Buffs.Where(b => !b.Name.StartsWith('#'))
            .GroupBy(b => (b.Name, b.Debuff)).Select(g => g.OrderByDescending(b => b.LeftMs(now)).First())
            .OrderByDescending(b => b.Debuff).ThenBy(b => b.LeftMs(now)).Take(10).ToList();
        if (buffs.Count > 0)
        {
            var wrap = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
            foreach (var b in buffs)
            {
                var badge = new BuffBadge { Width = 20, Height = 20, Margin = new Thickness(0, 0, 3, 0), ToolTip = b.Name + (b.Debuff ? " ⚠" : "") };
                var left = b.EndMs > 0 && b.DurationMs > 0 ? (double)b.LeftMs(now) / b.DurationMs : 1;
                var skill = _meter.Tracker.BuffSkill(b.Code);
                badge.Set(BuffBadge.KindOf(b.Name), left, 1, skill != 0 ? _meter.Icons.Get(skill) : null);
                var cell = new Grid();
                cell.Children.Add(badge);
                if (b.Debuff) cell.Children.Add(new Border { BorderBrush = Dead, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 3, 0) });
                wrap.Children.Add(cell);
            }
            mid.Children.Add(wrap);
        }

        // cooldowns under the row: the skill's icon with the time left on it — all of yours (from 1 s), the others' long ones
        if (m.Cooldowns.Count > 0)
        {
            var cds = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (var c in m.Cooldowns.Take(m.IsSelf ? 12 : 6)) cds.Children.Add(Cooldown(c, m.IsSelf ? 28 : 24));
            mid.Children.Add(cds);
        }
        Grid.SetColumn(mid, 1);
        line.Children.Add(mid);
        box.Children.Add(line);
        return box;
    }

    private UIElement Cooldown(PartyCooldown c, double size)
    {
        var cell = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 4, 2), ToolTip = $"{c.Skill} · {Math.Ceiling(c.LeftMs / 1000.0)} s" };
        var badge = new CooldownBadge { Margin = new Thickness(0), LayoutTransform = new ScaleTransform(size / 34, size / 34) };
        badge.Set(_meter.Icons.Get(c.SkillCode), c.TotalMs > 0 ? (double)c.LeftMs / c.TotalMs : 0, c.LeftMs);
        cell.Children.Add(badge);
        return cell;
    }

    /// <summary>The window as a PNG for the stream page /party.</summary>
    private void CaptureStreamFrame()
    {
        if (!IsVisible || Content is not FrameworkElement root || root.ActualWidth < 1 || _encoding) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(root);
        bmp.Freeze();
        _encoding = true;
        var stream = _meter.Stream;
        Task.Run(() =>
        {
            try
            {
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bmp));
                using var ms = new System.IO.MemoryStream();
                png.Save(ms);
                stream.SetWindowFrame("party", ms.ToArray());
            }
            catch (Exception ex)
            {
                Log.Error("Party stream frame", ex);
            }
            finally
            {
                _encoding = false;
            }
        });
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
