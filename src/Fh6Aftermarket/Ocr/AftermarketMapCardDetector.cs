using System.Drawing;

namespace Fh6Aftermarket.Ocr;

public sealed record AftermarketMapCardRegion(
    Rectangle Header,
    Rectangle VehicleNameRegion);

public static class AftermarketMapCardDetector
{
    private const int CanonicalWidth = 1920;
    private const int CanonicalHeight = 1080;

    public static bool TryFind(Bitmap source, out AftermarketMapCardRegion? region)
    {
        region = null;
        var scaleX = source.Width / (double)CanonicalWidth;
        var scaleY = source.Height / (double)CanonicalHeight;
        if (Math.Abs(scaleX - scaleY) > 0.02)
        {
            return false;
        }

        var search = Scale(new Rectangle(400, 75, 700, 435), scaleX, scaleY);
        search = Rectangle.Intersect(search, new Rectangle(0, 0, source.Width, source.Height));
        if (search.Width <= 0 || search.Height <= 0)
        {
            return false;
        }

        var rowCounts = new int[search.Height];
        var rowLeft = Enumerable.Repeat(int.MaxValue, search.Height).ToArray();
        var rowRight = Enumerable.Repeat(int.MinValue, search.Height).ToArray();

        for (var localY = 0; localY < search.Height; localY++)
        {
            var y = search.Top + localY;
            for (var x = search.Left; x < search.Right; x++)
            {
                if (!IsHeaderBlue(source.GetPixel(x, y)))
                {
                    continue;
                }

                rowCounts[localY]++;
                rowLeft[localY] = Math.Min(rowLeft[localY], x);
                rowRight[localY] = Math.Max(rowRight[localY], x);
            }
        }

        var peakLocalY = Array.IndexOf(rowCounts, rowCounts.Max());
        var peakCount = rowCounts[peakLocalY];
        if (peakCount < 260 * scaleX)
        {
            return false;
        }

        var rowThreshold = Math.Max((int)Math.Round(220 * scaleX), (int)Math.Round(peakCount * 0.55));
        var topLocalY = peakLocalY;
        var bottomLocalY = peakLocalY;
        while (topLocalY > 0 && rowCounts[topLocalY - 1] >= rowThreshold)
        {
            topLocalY--;
        }

        while (bottomLocalY + 1 < rowCounts.Length && rowCounts[bottomLocalY + 1] >= rowThreshold)
        {
            bottomLocalY++;
        }

        var left = rowLeft[peakLocalY];
        var right = rowRight[peakLocalY] + 1;
        var header = Rectangle.FromLTRB(
            left,
            search.Top + topLocalY,
            right,
            search.Top + bottomLocalY + 1);

        var canonicalHeaderWidth = header.Width / scaleX;
        var canonicalHeaderHeight = header.Height / scaleY;
        if (canonicalHeaderWidth is < 320 or > 600 || canonicalHeaderHeight is < 18 or > 80)
        {
            return false;
        }

        var nameLeft = header.Left + (int)Math.Round(12 * scaleX);
        var nameTop = header.Bottom + (int)Math.Round(4 * scaleY);
        var nameWidth = Math.Min(
            header.Width - (int)Math.Round(24 * scaleX),
            (int)Math.Round(390 * scaleX));
        // Keep this crop on the vehicle-name line. A taller crop reaches the
        // localized body copy at high DPI and makes stable names look different.
        var nameHeight = (int)Math.Round(36 * scaleY);
        var nameRegion = Rectangle.Intersect(
            new Rectangle(nameLeft, nameTop, nameWidth, nameHeight),
            new Rectangle(0, 0, source.Width, source.Height));
        if (nameRegion.Width <= 0 || nameRegion.Height <= 0)
        {
            return false;
        }

        region = new AftermarketMapCardRegion(header, nameRegion);
        return true;
    }

    private static Rectangle Scale(Rectangle rectangle, double scaleX, double scaleY)
        => new(
            (int)Math.Round(rectangle.X * scaleX),
            (int)Math.Round(rectangle.Y * scaleY),
            (int)Math.Round(rectangle.Width * scaleX),
            (int)Math.Round(rectangle.Height * scaleY));

    private static bool IsHeaderBlue(Color color)
        => color.B >= 90 &&
           color.B >= color.R * 1.45 &&
           color.B >= color.G * 1.25 &&
           color.R <= 115;
}
