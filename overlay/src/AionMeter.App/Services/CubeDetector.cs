using System.Drawing;
using System.Drawing.Imaging;

namespace AionMeter.App.Services;

public enum CubeColor
{
    Red,
    Yellow,
}

/// <summary>
/// A Hidden Cube found on a captured frame. X / Y are the centre of the cube relative to the captured region's
/// top-left corner (physical pixels); Width / Height are its bounding box.
/// </summary>
public readonly record struct CubeTarget(int X, int Y, int Width, int Height, CubeColor Color, double Confidence);

/// <summary>
/// 1 HP: finds the red and yellow Hidden Cubes on a screenshot. The Python original used OpenCV; this meter is a
/// C# / WPF app with no native OpenCV, so the same logic is done directly on the pixels: colour thresholds, a
/// morphological erosion (solidity check), connected components (the contour equivalent) and shape / size filters
/// that drop HP bars, effects and the interface.
/// </summary>
public sealed class CubeDetector
{
    public sealed class Options
    {
        public int MinSize { get; init; } = 10;
        public int MaxSize { get; init; } = 60;

        // red cube: R > RedR, G &lt; RedG, B &lt; RedB
        public int RedR { get; init; } = 170;
        public int RedG { get; init; } = 100;
        public int RedB { get; init; } = 100;

        // yellow cube: R > YellowR, G > YellowG, B &lt; YellowB
        public int YellowR { get; init; } = 190;
        public int YellowG { get; init; } = 150;
        public int YellowB { get; init; } = 110;

        /// <summary>1..100; 50 keeps the thresholds as-is, higher relaxes them (finds cubes easier).</summary>
        public int Sensitivity { get; init; } = 50;

        public static Options From(AppSettings s) => new()
        {
            MinSize = Math.Clamp(s.CubeMinSize, 2, 200),
            MaxSize = Math.Clamp(s.CubeMaxSize, 2, 400),
            RedR = s.CubeRedR,
            RedG = s.CubeRedG,
            RedB = s.CubeRedB,
            YellowR = s.CubeYellowR,
            YellowG = s.CubeYellowG,
            YellowB = s.CubeYellowB,
            Sensitivity = Math.Clamp(s.CubeSensitivity, 1, 100),
        };
    }

    // Reusable buffers, resized on demand: the detector runs ~25 times a second and must not churn the GC.
    private byte[] _mask = new byte[0];
    private bool[] _seen = new bool[0];
    private int[] _fill = new int[0]; // flood-fill queue
    private int _w;
    private int _h;

    public unsafe List<CubeTarget> Detect(Bitmap frame, Options o)
    {
        var result = new List<CubeTarget>();
        var w = frame.Width;
        var h = frame.Height;
        if (w < 2 || h < 2) return result;

        EnsureBuffers(w, h);
        var mask = _mask;
        var seen = _seen;
        Array.Clear(mask, 0, mask.Length);
        Array.Clear(seen, 0, seen.Length);

        // Sensitivity: 50 = exact thresholds; higher relaxes ">" down and "<" up so more pixels match.
        var relax = (o.Sensitivity - 50) / 50.0 * 0.4;
        var redR = Clamp(o.RedR * (1 - relax));
        var redG = Clamp(o.RedG * (1 + relax));
        var redB = Clamp(o.RedB * (1 + relax));
        var yelR = Clamp(o.YellowR * (1 - relax));
        var yelG = Clamp(o.YellowG * (1 - relax));
        var yelB = Clamp(o.YellowB * (1 + relax));

        var data = frame.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var p0 = (byte*)data.Scan0;
            var stride = data.Stride;
            for (var y = 0; y < h; y++)
            {
                var row = p0 + y * stride;
                var idx = y * w;
                for (var x = 0; x < w; x++, idx++)
                {
                    var b = row[x * 4];
                    var g = row[x * 4 + 1];
                    var r = row[x * 4 + 2]; // Format32bppArgb is BGRA in memory
                    if (r > redR && g < redG && b < redB) mask[idx] = 1;
                    else if (r > yelR && g > yelG && b < yelB) mask[idx] = 2;
                }
            }
        }
        finally
        {
            frame.UnlockBits(data);
        }

        // Connected components over the matched pixels (4-connectivity, so cubes side by side stay apart).
        for (var i = 0; i < mask.Length; i++)
        {
            if (mask[i] == 0 || seen[i]) continue;
            var (minX, minY, maxX, maxY, area, red, yellow) = Flood(i);
            var bw = maxX - minX + 1;
            var bh = maxY - minY + 1;
            if (bw < o.MinSize || bh < o.MinSize || bw > o.MaxSize || bh > o.MaxSize) continue; // drops big areas & effects

            // Near-square / rhombus: HP bars are wide and thin, a cube's box is about square.
            var aspect = bw >= bh ? bw / (double)bh : bh / (double)bw;
            if (aspect > 1.7) continue;

            // Fill ratio: a solid cube fills most of its box; a thin outline or a stray line does not.
            var fill = area / (double)(bw * bh);
            if (fill is < 0.15 or > 0.95) continue;

            // Morphological check: how much of the blob survives a one-pixel erosion (all four neighbours set).
            // Speckle noise and one-pixel lines erode to nothing; a solid cube keeps most of its pixels.
            var solid = Solidity(minX, minY, maxX, maxY);
            if (solid < 0.2) continue;

            var color = red >= yellow ? CubeColor.Red : CubeColor.Yellow;
            var purity = Math.Max(red, yellow) / (double)area;
            var confidence = Math.Clamp(fill * (0.5 + 0.5 / aspect) * Math.Min(1, solid * 1.4) * (0.5 + 0.5 * purity), 0, 1);
            result.Add(new CubeTarget((minX + maxX) / 2, (minY + maxY) / 2, bw, bh, color, Math.Round(confidence, 2)));
        }
        return result;

        // Flood-fill from a seed, returning the component's bounding box, area and red / yellow counts.
        (int minX, int minY, int maxX, int maxY, int area, int red, int yellow) Flood(int seed)
        {
            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;
            var area = 0;
            var red = 0;
            var yellow = 0;
            var head = 0;
            var tail = 0;
            _fill[tail++] = seed;
            seen[seed] = true;
            while (head < tail)
            {
                var idx = _fill[head++];
                var x = idx % _w;
                var y = idx / _w;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
                area++;
                if (mask[idx] == 1) red++;
                else yellow++;

                if (x > 0 && !seen[idx - 1] && mask[idx - 1] != 0) { seen[idx - 1] = true; _fill[tail++] = idx - 1; }
                if (x < _w - 1 && !seen[idx + 1] && mask[idx + 1] != 0) { seen[idx + 1] = true; _fill[tail++] = idx + 1; }
                if (y > 0 && !seen[idx - _w] && mask[idx - _w] != 0) { seen[idx - _w] = true; _fill[tail++] = idx - _w; }
                if (y < _h - 1 && !seen[idx + _w] && mask[idx + _w] != 0) { seen[idx + _w] = true; _fill[tail++] = idx + _w; }
            }
            return (minX, minY, maxX, maxY, area, red, yellow);
        }

        // Fraction of the component's pixels whose four neighbours are all part of the same mask (one-pixel erosion).
        double Solidity(int minX, int minY, int maxX, int maxY)
        {
            var total = 0;
            var core = 0;
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    var idx = y * _w + x;
                    if (mask[idx] == 0) continue;
                    total++;
                    if (x > minX && x < maxX && y > minY && y < maxY &&
                        mask[idx - 1] != 0 && mask[idx + 1] != 0 && mask[idx - _w] != 0 && mask[idx + _w] != 0)
                        core++;
                }
            }
            return total == 0 ? 0 : core / (double)total;
        }
    }

    private void EnsureBuffers(int w, int h)
    {
        var size = w * h;
        if (_mask.Length == size && _w == w) return;
        _mask = new byte[size];
        _seen = new bool[size];
        _fill = new int[size];
        _w = w;
        _h = h;
    }

    private static int Clamp(double v) => (int)Math.Round(Math.Clamp(v, 0, 255));
}
