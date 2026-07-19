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
    int BrightPixelCount,
    double FillRatio,
    double SymmetryScore,
    double ShapeScore);

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
        var candidates = FindGlobeComponents(bitmap, markerRegion, geometry);
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

        return MarkerVisualState.Clear;
    }

    private static IReadOnlyList<MarkerCandidate> FindGlobeComponents(
        Bitmap bitmap,
        PixelRect region,
        ScreenGeometry geometry)
    {
        var pixels = ReadPixels(bitmap);
        var mask = new bool[region.Width * region.Height];

        for (var y = 0; y < region.Height; y++)
        {
            for (var x = 0; x < region.Width; x++)
            {
                var sourceX = region.X + x;
                var sourceY = region.Y + y;
                var color = pixels[sourceY * bitmap.Width + sourceX];
                var index = y * region.Width + x;
                mask[index] = Luminance(color) >= 0.78;
            }
        }

        var visited = new bool[mask.Length];
        var queue = new int[mask.Length];
        var results = new List<MarkerCandidate>();
        var scale = geometry.Width / (double)ScreenGeometry.CanonicalWidth;
        var minArea = Math.Max(35, (int)Math.Round(95 * scale * scale));

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
            var componentPixels = new List<int>(256);

            while (head < tail)
            {
                var current = queue[head++];
                var x = current % region.Width;
                var y = current / region.Width;

                count++;
                componentPixels.Add(current);
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
                for (var offsetY = -1; offsetY <= 1; offsetY++)
                {
                    for (var offsetX = -1; offsetX <= 1; offsetX++)
                    {
                        if (offsetX != 0 || offsetY != 0)
                        {
                            Visit(x + offsetX, y + offsetY);
                        }
                    }
                }
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

            var fillRatio = count / (double)(width * height);
            if (canonicalWidth is < 28 or > 78 ||
                canonicalHeight is < 28 or > 82 ||
                aspect is < 0.68 or > 1.32 ||
                fillRatio is < 0.09 or > 0.58)
            {
                continue;
            }

            var bounds = new PixelRect(
                region.X + minX,
                region.Y + minY,
                width,
                height);

            var center = new PixelPoint(
                bounds.X + bounds.Width / 2,
                bounds.Y + bounds.Height / 2);
            var squareness = 1 - Math.Min(1, Math.Abs(1 - aspect));
            var localMask = new bool[width * height];
            foreach (var pixel in componentPixels)
            {
                var pixelX = pixel % region.Width;
                var pixelY = pixel / region.Width;
                localMask[(pixelY - minY) * width + pixelX - minX] = true;
            }

            var horizontalMatches = 0;
            var verticalMatches = 0;
            foreach (var pixel in componentPixels)
            {
                var pixelX = pixel % region.Width - minX;
                var pixelY = pixel / region.Width - minY;
                if (localMask[pixelY * width + (width - 1 - pixelX)])
                {
                    horizontalMatches++;
                }

                if (localMask[(height - 1 - pixelY) * width + pixelX])
                {
                    verticalMatches++;
                }
            }

            var symmetryScore = (horizontalMatches + verticalMatches) / (2d * count);
            var shapeScore = squareness * 0.25 +
                             Math.Min(1, fillRatio / 0.30) * 0.15 +
                             symmetryScore * 0.60;

            results.Add(new MarkerCandidate(
                center,
                bounds,
                count,
                fillRatio,
                symmetryScore,
                shapeScore));

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

        return results
            .Where(candidate =>
                candidate.SymmetryScore >= 0.86 &&
                candidate.ShapeScore >= 0.90)
            .OrderByDescending(candidate => candidate.ShapeScore)
            .ThenByDescending(candidate => candidate.BrightPixelCount)
            .Take(1)
            .ToArray();
    }

    private static double Luminance(RgbColor color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255d;

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
