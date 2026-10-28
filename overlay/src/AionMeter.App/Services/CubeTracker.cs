using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using AionMeter.App.Windows;

namespace AionMeter.App.Services;

/// <summary>A screen rectangle in physical pixels (the unit GDI capture uses).</summary>
public readonly record struct ScreenRegion(int Left, int Top, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public double CenterX => Left + Width / 2.0;
    public double CenterY => Top + Height / 2.0;
}

/// <summary>
/// GDI screen capture — the C# equivalent of Python's <c>mss</c>. <see cref="Graphics.CopyFromScreen"/> reads the
/// desktop in physical pixels, which is what the cube detector expects. A 0×0 region means the whole virtual screen.
/// </summary>
public static class ScreenCapture
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);

    /// <summary>The whole virtual desktop in physical pixels.</summary>
    public static ScreenRegion VirtualScreen() => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN),
        GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN),
        GetSystemMetrics(SM_CYVIRTUALSCREEN));

    /// <summary>The saved region, or the whole virtual screen when nothing (or 0×0) is saved.</summary>
    public static ScreenRegion ResolveRegion(AppSettings s)
    {
        if (s.CubeWidth <= 0 || s.CubeHeight <= 0) return VirtualScreen();
        var v = VirtualScreen();
        var left = (int)Math.Round(s.CubeLeft);
        var top = (int)Math.Round(s.CubeTop);
        var w = (int)Math.Round(s.CubeWidth);
        var h = (int)Math.Round(s.CubeHeight);
        // Keep the capture inside the desktop, in case a monitor was unplugged since the region was saved.
        left = Math.Clamp(left, v.Left, v.Left + v.Width - 1);
        top = Math.Clamp(top, v.Top, v.Top + v.Height - 1);
        w = Math.Clamp(w, 1, v.Left + v.Width - left);
        h = Math.Clamp(h, 1, v.Top + v.Height - top);
        return new ScreenRegion(left, top, w, h);
    }

    /// <summary>Every monitor, in physical pixels, for the settings' monitor list.</summary>
    public static IReadOnlyList<ScreenRegion> Monitors() =>
        System.Windows.Forms.Screen.AllScreens
            .Select(m => new ScreenRegion(m.Bounds.Left, m.Bounds.Top, m.Bounds.Width, m.Bounds.Height))
            .ToList();

    public static Bitmap Capture(ScreenRegion region)
    {
        var bmp = new Bitmap(Math.Max(1, region.Width), Math.Max(1, region.Height), PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(region.Left, region.Top, 0, 0, new Size(region.Width, region.Height), CopyPixelOperation.SourceCopy);
        return bmp;
    }
}

/// <summary>
/// 1 HP: every 40 ms screenshots the chosen area, looks for Hidden Cubes and points the arrow overlay at the one
/// nearest to the centre of the screen. Runs on its own thread (the C# equivalent of the Python
/// <c>threading + Qt signal</c>), so the DPS meter's UI is never blocked; results are marshalled to the overlay's
/// dispatcher. A failed screenshot is logged and skipped, never fatal.
/// </summary>
public sealed class CubeTracker : IDisposable
{
    private readonly AppSettings _settings;
    private readonly CubeArrowOverlay _overlay;
    private readonly CubeDetector _detector = new();
    private readonly Dispatcher _ui;
    private Thread? _thread;
    private volatile bool _running;

    private long _frames;
    private long _windowStart;
    private long _lastCubeLog;
    private long _lastFpsLog;

    public CubeTracker(AppSettings settings, CubeArrowOverlay overlay)
    {
        _settings = settings;
        _overlay = overlay;
        _ui = overlay.Dispatcher;
    }

    public bool IsRunning => _running;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _frames = 0;
        _windowStart = _lastCubeLog = _lastFpsLog = Environment.TickCount64;
        _thread = new Thread(Loop) { IsBackground = true, Name = "CubeTracker" };
        _thread.Start();
        Log.Info("Cube tracker started");
    }

    public void Stop()
    {
        if (!_running) return;
        _running = false;
        try
        {
            _thread?.Join(1500);
        }
        catch (Exception)
        {
        }
        _thread = null;
        try
        {
            _ui.BeginInvoke(() => _overlay.UpdateTarget(null, default));
        }
        catch (Exception)
        {
            // the dispatcher may already be shutting down when the app exits
        }
        Log.Info("Cube tracker stopped");
    }

    public void Dispose() => Stop();

    private void Loop()
    {
        while (_running)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var region = ScreenCapture.ResolveRegion(_settings);
                using var frame = ScreenCapture.Capture(region);
                var targets = _detector.Detect(frame, CubeDetector.Options.From(_settings));
                var best = NearestToCenter(targets, region);
                var angle = best is { } b
                    ? Math.Atan2(b.Y - region.Height / 2.0, b.X - region.Width / 2.0) * 180.0 / Math.PI
                    : 0.0;
                LogFrame(best, angle);
                // Push to the UI only while the feature and the overlay are both on; otherwise the arrow stays hidden.
                var target = _settings.CubeEnabled && _settings.CubeOverlayOn && !_settings.NoOverlay ? best : null;
                _ui.BeginInvoke(() => _overlay.UpdateTarget(target, region));
            }
            catch (Exception ex)
            {
                Log.Error("Cube screenshot failed", ex);
            }
            _frames++;
            var wait = 40 - sw.ElapsedMilliseconds;
            if (wait > 0) Thread.Sleep((int)wait);
        }
    }

    private static CubeTarget? NearestToCenter(List<CubeTarget> targets, ScreenRegion region)
    {
        if (targets.Count == 0) return null;
        var cx = region.Width / 2.0;
        var cy = region.Height / 2.0;
        var best = targets[0];
        var bestD = double.MaxValue;
        foreach (var t in targets)
        {
            var dx = t.X - cx;
            var dy = t.Y - cy;
            var d = dx * dx + dy * dy;
            if (d >= bestD) continue;
            bestD = d;
            best = t;
        }
        return best;
    }

    private void LogFrame(CubeTarget? best, double angle)
    {
        var now = Environment.TickCount64;
        if (best is { } b && now - _lastCubeLog >= 1000)
        {
            _lastCubeLog = now;
            Log.Info($"Cube {b.Color} at ({b.X}, {b.Y}) size {b.Width}x{b.Height} conf {b.Confidence:0.00} angle {angle:0.#}° fps {Fps}");
        }
        if (now - _lastFpsLog >= 5000)
        {
            _lastFpsLog = now;
            Log.Info($"Cube tracker fps {Fps}");
        }
    }

    private double Fps
    {
        get
        {
            var secs = (Environment.TickCount64 - _windowStart) / 1000.0;
            return secs <= 0 ? 0 : Math.Round(_frames / secs, 1);
        }
    }
}
