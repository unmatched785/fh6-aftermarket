using System.Drawing;

namespace Fh6Aftermarket.Vision;

public sealed record AftermarketMapIconCluster(
    Rectangle Bounds,
    int GreenPixelCount,
    IReadOnlyList<Point> ClickTargets);

public sealed record AftermarketMapIconClusterObservation(
    IReadOnlyList<AftermarketMapIconCluster> Candidates)
{
    public bool HasSingleCluster => Candidates.Count == 1;
}

public static class AftermarketMapIconClusterDetector
{
    private const int CanonicalWidth = 1920;
    private const int CanonicalHeight = 1080;

    public static AftermarketMapIconClusterObservation Inspect(Bitmap source)
    {
        var scaleX = source.Width / (double)CanonicalWidth;
        var scaleY = source.Height / (double)CanonicalHeight;
        if (Math.Abs(scaleX - scaleY) > 0.02)
        {
            return new AftermarketMapIconClusterObservation([]);
        }

        // The zoom step first places the cursor on the detected dealership area.
        // Limiting the search to that normalized neighborhood prevents bright farm
        // textures elsewhere on the map from being mistaken for sale icons.
        var search = Scale(new Rectangle(600, 350, 400, 300), scaleX, scaleY);
        search = Rectangle.Intersect(search, new Rectangle(0, 0, source.Width, source.Height));
        var visited = new bool[source.Width * source.Height];
        var queue = new Queue<int>();
        var components = new List<GreenComponent>();

        for (var y = search.Top; y < search.Bottom; y++)
        {
            for (var x = search.Left; x < search.Right; x++)
            {
                var index = y * source.Width + x;
                if (visited[index])
                {
                    continue;
                }

                visited[index] = true;
                if (!IsIconGreen(source.GetPixel(x, y)))
                {
                    continue;
                }

                var left = x;
                var right = x;
                var top = y;
                var bottom = y;
                var count = 0;
                queue.Enqueue(index);

                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    var currentY = current / source.Width;
                    var currentX = current - currentY * source.Width;
                    count++;
                    left = Math.Min(left, currentX);
                    right = Math.Max(right, currentX);
                    top = Math.Min(top, currentY);
                    bottom = Math.Max(bottom, currentY);

                    Visit(currentX - 1, currentY);
                    Visit(currentX + 1, currentY);
                    Visit(currentX, currentY - 1);
                    Visit(currentX, currentY + 1);
                }

                var bounds = Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
                if (count / (scaleX * scaleY) >= 3 &&
                    bounds.Width / scaleX >= 1 &&
                    bounds.Height / scaleY >= 1)
                {
                    components.Add(new GreenComponent(bounds, count));
                }

                void Visit(int neighborX, int neighborY)
                {
                    if (!search.Contains(neighborX, neighborY))
                    {
                        return;
                    }

                    var neighbor = neighborY * source.Width + neighborX;
                    if (visited[neighbor])
                    {
                        return;
                    }

                    visited[neighbor] = true;
                    if (IsIconGreen(source.GetPixel(neighborX, neighborY)))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        var mergePadding = (int)Math.Round(28 * scaleX);
        var merged = new List<GreenComponent>();
        foreach (var component in components.OrderByDescending(item => item.GreenPixelCount))
        {
            var matches = merged
                .Select((item, index) => new { item, index })
                .Where(pair => Rectangle.Intersect(
                    Inflate(pair.item.Bounds, mergePadding, source.Width, source.Height),
                    component.Bounds).Width > 0)
                .Select(pair => pair.index)
                .OrderByDescending(index => index)
                .ToArray();

            var combined = component;
            foreach (var index in matches)
            {
                combined = new GreenComponent(
                    Rectangle.Union(combined.Bounds, merged[index].Bounds),
                    combined.GreenPixelCount + merged[index].GreenPixelCount);
                merged.RemoveAt(index);
            }

            merged.Add(combined);
        }

        var candidates = merged
            .Where(component =>
            {
                var width = component.Bounds.Width / scaleX;
                var height = component.Bounds.Height / scaleY;
                var area = component.GreenPixelCount / (scaleX * scaleY);
                return width is >= 35 and <= 150 &&
                       height is >= 35 and <= 150 &&
                       area >= 300;
            })
            .OrderByDescending(component => component.GreenPixelCount)
            .Select(component => new AftermarketMapIconCluster(
                component.Bounds,
                component.GreenPixelCount,
                CreateClickTargets(component.Bounds)))
            .ToArray();

        return new AftermarketMapIconClusterObservation(candidates);
    }

    private static IReadOnlyList<Point> CreateClickTargets(Rectangle bounds)
        =>
        [
            RelativePoint(bounds, 0.52, 0.27),
            RelativePoint(bounds, 0.23, 0.64),
            RelativePoint(bounds, 0.53, 0.44)
        ];

    private static Point RelativePoint(Rectangle bounds, double xRatio, double yRatio)
        => new(
            bounds.Left + (int)Math.Round(bounds.Width * xRatio),
            bounds.Top + (int)Math.Round(bounds.Height * yRatio));

    private static bool IsIconGreen(Color color)
        => color.R <= 100 &&
           color.G is >= 140 and <= 205 &&
           color.B is >= 80 and <= 150 &&
           color.G - color.R >= 65 &&
           color.G - color.B is >= 25 and <= 80 &&
           color.B - color.R >= 25;

    private static Rectangle Scale(Rectangle rectangle, double scaleX, double scaleY)
        => new(
            (int)Math.Round(rectangle.X * scaleX),
            (int)Math.Round(rectangle.Y * scaleY),
            (int)Math.Round(rectangle.Width * scaleX),
            (int)Math.Round(rectangle.Height * scaleY));

    private static Rectangle Inflate(Rectangle rectangle, int padding, int width, int height)
        => Rectangle.Intersect(
            Rectangle.FromLTRB(
                rectangle.Left - padding,
                rectangle.Top - padding,
                rectangle.Right + padding,
                rectangle.Bottom + padding),
            new Rectangle(0, 0, width, height));

    private sealed record GreenComponent(Rectangle Bounds, int GreenPixelCount);
}
