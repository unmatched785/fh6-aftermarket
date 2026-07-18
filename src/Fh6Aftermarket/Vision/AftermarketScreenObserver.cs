using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Fh6Aftermarket.Domain;

namespace Fh6Aftermarket.Vision;

public enum MarkerVisualState
{
    None,
    Translucent,
    Clear,
    SelectedCard
}

public sealed record MarkerCandidate(
    PixelPoint Center,
    PixelRect Bounds,
    int PurplePixelCount,
    double MeanSaturation,
    double MeanValue);

public sealed record ScreenObservation(
    int Width,
    int Height,
    bool SupportedAspectRatio,
    MarkerVisualState State,
    IReadOnlyList<MarkerCandidate> Candidates,
    double CardBrightCoverage);

public static class AftermarketScreenObserver
{
    public static ScreenObservation Observe(Bitmap source)
    {
        if (!ScreenGeometry.TryCreate(source.Width, source.Height, out var geometry) || geometry is null)
        {
            return new ScreenObservation(
                source.Width,
                source.Height,
                false,
                MarkerVisualState.None,
                [],
                0);
        }

        using var bitmap = To24Bit(source);
        var markerRegion = geometry.Scale(AnchorCatalog.AftermarketMarkerSearch);
        var candidates = FindPurpleComponents(bitmap, markerRegion, geometry);
        var cardCoverage = MeasureBrightNeutralCoverage(
            bitmap,
            geometry.Scale(AnchorCatalog.AftermarketLocationCard));

        var state = Classify(candidates, cardCoverage, geometry);
        return new ScreenObservation(
            source.Width,
            source.Height,
            true,
            state,
            candidates,
            cardCoverage);
    }

    private static MarkerVisualState Classify(
        IReadOnlyList<MarkerCandidate> candidates,
        double cardCoverage,
        ScreenGeometry geometry)
    {
        if (candidates.Count != 1)
        {
            return MarkerVisualState.None;
        }

        if (cardCoverage >= 0.18)
        {
            return MarkerVisualState.SelectedCard;
        }

        var candidate = candidates[0];
        var scale = geometry.Width / (double)ScreenGeometry.CanonicalWidth;
        var canonicalPurpleArea = candidate.PurplePixelCount / (scale * scale);

        return canonicalPurpleArea >= 460 &&
               candidate.MeanSaturation >= 0.63 &&
               candidate.MeanValue >= 0.42
            ? MarkerVisualState.Clear
            : MarkerVisualState.Translucent;
    }

    private static IReadOnlyList<MarkerCandidate> FindPurpleComponents(
        Bitmap bitmap,
        PixelRect region,
        ScreenGeometry geometry)
    {
        var pixels = ReadPixels(bitmap);
        var mask = new bool[region.Width * region.Height];
        var saturation = new double[mask.Length];
        var value = new double[mask.Length];

        for (var y = 0; y < region.Height; y++)
        {
            for (var x = 0; x < region.Width; x++)
            {
                var sourceX = region.X + x;
                var sourceY = region.Y + y;
                var color = pixels[sourceY * bitmap.Width + sourceX];
                var hsv = ToHsv(color.R, color.G, color.B);
                var index = y * region.Width + x;

                saturation[index] = hsv.Saturation;
                value[index] = hsv.Value;
                mask[index] = hsv.Hue is >= 245 and <= 325 &&
                              hsv.Saturation >= 0.28 &&
                              hsv.Value >= 0.25;
            }
        }

        var visited = new bool[mask.Length];
        var queue = new int[mask.Length];
        var results = new List<MarkerCandidate>();
        var scale = geometry.Width / (double)ScreenGeometry.CanonicalWidth;
        var minArea = Math.Max(40, (int)Math.Round(120 * scale * scale));

        for (var index = 0; index < mask.Length; index++)
        {
            if (!mask[index] || visited[index])
            {
                continue;
            }

            var head = 0;
            var tail = 0;
            queue[tail++] = index;
            visited[index] = true;

            var count = 0;
            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;
            var saturationSum = 0d;
            var valueSum = 0d;

            while (head < tail)
            {
                var current = queue[head++];
                var x = current % region.Width;
                var y = current / region.Width;

                count++;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                saturationSum += saturation[current];
                valueSum += value[current];

                Visit(x - 1, y);
                Visit(x + 1, y);
                Visit(x, y - 1);
                Visit(x, y + 1);
            }

            if (count < minArea)
            {
                continue;
            }

            var width = maxX - minX + 1;
            var height = maxY - minY + 1;
            var canonicalWidth = width / scale;
            var canonicalHeight = height / scale;
            var aspect = width / (double)height;

            if (canonicalWidth is < 20 or > 115 ||
                canonicalHeight is < 20 or > 115 ||
                aspect is < 0.60 or > 1.40)
            {
                continue;
            }

            var bounds = new PixelRect(
                region.X + minX,
                region.Y + minY,
                width,
                height);

            results.Add(new MarkerCandidate(
                new PixelPoint(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2),
                bounds,
                count,
                saturationSum / count,
                valueSum / count));

            void Visit(int neighborX, int neighborY)
            {
                if (neighborX < 0 || neighborY < 0 ||
                    neighborX >= region.Width || neighborY >= region.Height)
                {
                    return;
                }

                var neighbor = neighborY * region.Width + neighborX;
                if (!visited[neighbor] && mask[neighbor])
                {
                    visited[neighbor] = true;
                    queue[tail++] = neighbor;
                }
            }
        }

        var expectedCenter = geometry.Scale(AnchorCatalog.AftermarketMarkerExpectedCenter);
        var maximumDistance = 100 * scale;

        return results
            .Where(candidate => Distance(candidate.Center, expectedCenter) <= maximumDistance)
            .OrderByDescending(candidate => candidate.PurplePixelCount)
            .ToArray();
    }

    private static double Distance(PixelPoint left, PixelPoint right)
    {
        var deltaX = left.X - right.X;
        var deltaY = left.Y - right.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static double MeasureBrightNeutralCoverage(Bitmap bitmap, PixelRect region)
    {
        var pixels = ReadPixels(bitmap);
        var brightNeutral = 0;
        var total = region.Width * region.Height;

        for (var y = region.Y; y < region.Bottom; y++)
        {
            for (var x = region.X; x < region.Right; x++)
            {
                var color = pixels[y * bitmap.Width + x];
                var hsv = ToHsv(color.R, color.G, color.B);
                if (hsv.Value >= 0.72 && hsv.Saturation <= 0.30)
                {
                    brightNeutral++;
                }
            }
        }

        return total == 0 ? 0 : brightNeutral / (double)total;
    }

    private static Bitmap To24Bit(Bitmap source)
    {
        var converted = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(converted);
        graphics.DrawImageUnscaled(source, 0, 0);
        return converted;
    }

    private static RgbColor[] ReadPixels(Bitmap bitmap)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format24bppRgb);

        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var result = new RgbColor[bitmap.Width * bitmap.Height];

            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = data.Stride >= 0 ? y * data.Stride : (bitmap.Height - 1 - y) * -data.Stride;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var offset = row + x * 3;
                    result[y * bitmap.Width + x] = new RgbColor(
                        bytes[offset + 2],
                        bytes[offset + 1],
                        bytes[offset]);
                }
            }

            return result;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static HsvColor ToHsv(byte red, byte green, byte blue)
    {
        var r = red / 255d;
        var g = green / 255d;
        var b = blue / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        double hue;

        if (delta == 0)
        {
            hue = 0;
        }
        else if (max == r)
        {
            hue = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            hue = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            hue = 60 * (((r - g) / delta) + 4);
        }

        if (hue < 0)
        {
            hue += 360;
        }

        return new HsvColor(hue, max == 0 ? 0 : delta / max, max);
    }

    private readonly record struct RgbColor(byte R, byte G, byte B);

    private readonly record struct HsvColor(double Hue, double Saturation, double Value);
}
