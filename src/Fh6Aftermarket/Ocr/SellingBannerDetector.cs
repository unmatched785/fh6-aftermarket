using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Fh6Aftermarket.Ocr;

public sealed record SellingBannerRegion(
    Rectangle GreenLine,
    Rectangle TextRegion);

public static class SellingBannerDetector
{
    private const int CanonicalWidth = 1920;
    private const int CanonicalHeight = 1080;

    public static IReadOnlyList<SellingBannerRegion> Find(Bitmap source)
    {
        using var bitmap = To24Bit(source);
        var pixels = ReadPixels(bitmap);
        var scale = Math.Min(
            bitmap.Width / (double)CanonicalWidth,
            bitmap.Height / (double)CanonicalHeight);

        var minimumRun = Math.Max(36, (int)Math.Round(125 * scale));
        var maximumGap = Math.Max(1, (int)Math.Round(2 * scale));
        var scanBottom = Math.Min(bitmap.Height, (int)Math.Round(bitmap.Height * 0.72));
        var groups = new List<LineGroup>();

        for (var y = 0; y < scanBottom; y++)
        {
            var x = 0;
            while (x < bitmap.Width)
            {
                if (!IsSellingGreen(pixels[y * bitmap.Width + x]))
                {
                    x++;
                    continue;
                }

                var start = x;
                var lastGreen = x;
                var greenPixels = 0;
                var gap = 0;

                while (x < bitmap.Width && gap <= maximumGap)
                {
                    if (IsSellingGreen(pixels[y * bitmap.Width + x]))
                    {
                        lastGreen = x;
                        greenPixels++;
                        gap = 0;
                    }
                    else
                    {
                        gap++;
                    }

                    x++;
                }

                var width = lastGreen - start + 1;
                if (width < minimumRun || greenPixels / (double)width < 0.82)
                {
                    continue;
                }

                AddRun(groups, start, lastGreen, y);
            }
        }

        var results = new List<SellingBannerRegion>();
        foreach (var group in groups)
        {
            var line = group.Bounds;
            var canonicalWidth = line.Width / scale;
            var canonicalHeight = line.Height / scale;

            if (canonicalWidth is < 125 or > 1200 || canonicalHeight is < 1 or > 24)
            {
                continue;
            }

            if (!HasGreenSaleIconToLeft(pixels, bitmap.Width, bitmap.Height, line, scale))
            {
                continue;
            }

            var leftPadding = (int)Math.Round(24 * scale);
            var rightPadding = (int)Math.Round(90 * scale);
            var top = line.Bottom + Math.Max(1, (int)Math.Round(1 * scale));
            var height = Math.Max(78, (int)Math.Round(78 * scale));
            var textRegion = Clamp(
                new Rectangle(
                    line.Left - leftPadding,
                    top,
                    line.Width + leftPadding + rightPadding,
                    height),
                bitmap.Width,
                bitmap.Height);

            if (textRegion.Width > 0 && textRegion.Height > 0)
            {
                results.Add(new SellingBannerRegion(line, textRegion));
            }
        }

        return results
            .OrderBy(region => region.GreenLine.Top)
            .ThenBy(region => region.GreenLine.Left)
            .ToArray();
    }

    private static void AddRun(List<LineGroup> groups, int start, int end, int y)
    {
        var best = groups
            .Where(group => y - group.Bottom <= 2)
            .Select(group => new
            {
                Group = group,
                Overlap = Math.Max(0, Math.Min(end, group.Right) - Math.Max(start, group.Left) + 1)
            })
            .Where(item => item.Overlap >= Math.Min(end - start + 1, item.Group.Width) * 0.65)
            .OrderByDescending(item => item.Overlap)
            .FirstOrDefault();

        if (best is null)
        {
            groups.Add(new LineGroup(start, end, y));
            return;
        }

        best.Group.Add(start, end, y);
    }

    private static bool HasGreenSaleIconToLeft(
        RgbColor[] pixels,
        int imageWidth,
        int imageHeight,
        Rectangle line,
        double scale)
    {
        var search = Clamp(
            new Rectangle(
                line.Left - (int)Math.Round(175 * scale),
                line.Top - (int)Math.Round(150 * scale),
                (int)Math.Round(275 * scale),
                (int)Math.Round(230 * scale)),
            imageWidth,
            imageHeight);

        var greenPixels = 0;
        for (var y = search.Top; y < search.Bottom; y++)
        {
            for (var x = search.Left; x < search.Right; x++)
            {
                if (IsSellingGreen(pixels[y * imageWidth + x]))
                {
                    greenPixels++;
                }
            }
        }

        var canonicalArea = greenPixels / Math.Max(0.01, scale * scale);
        return canonicalArea >= 280;
    }

    private static bool IsSellingGreen(RgbColor color)
    {
        return color.G >= 80 &&
               color.G >= color.R + 24 &&
               color.G >= color.B + 12 &&
               color.R <= 145;
    }

    private static Rectangle Clamp(Rectangle rectangle, int width, int height)
    {
        var left = Math.Clamp(rectangle.Left, 0, width);
        var top = Math.Clamp(rectangle.Top, 0, height);
        var right = Math.Clamp(rectangle.Right, left, width);
        var bottom = Math.Clamp(rectangle.Bottom, top, height);
        return Rectangle.FromLTRB(left, top, right, bottom);
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
                var row = data.Stride >= 0
                    ? y * data.Stride
                    : (bitmap.Height - 1 - y) * -data.Stride;

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

    private sealed class LineGroup
    {
        public LineGroup(int start, int end, int y)
        {
            Left = start;
            Right = end;
            Top = y;
            Bottom = y;
        }

        public int Left { get; private set; }
        public int Right { get; private set; }
        public int Top { get; }
        public int Bottom { get; private set; }
        public int Width => Right - Left + 1;
        public Rectangle Bounds => Rectangle.FromLTRB(Left, Top, Right + 1, Bottom + 1);

        public void Add(int start, int end, int y)
        {
            Left = Math.Min(Left, start);
            Right = Math.Max(Right, end);
            Bottom = Math.Max(Bottom, y);
        }
    }

    private readonly record struct RgbColor(byte R, byte G, byte B);
}
