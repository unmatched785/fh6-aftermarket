namespace Fh6Aftermarket.Domain;

public readonly record struct PixelPoint(int X, int Y);

public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;
}

public sealed class ScreenGeometry
{
    public const int CanonicalWidth = 1920;
    public const int CanonicalHeight = 1080;
    public const double DefaultAspectTolerance = 0.005;

    private ScreenGeometry(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    public static bool TryCreate(
        int width,
        int height,
        out ScreenGeometry? geometry,
        double tolerance = DefaultAspectTolerance)
    {
        geometry = null;

        if (width <= 0 || height <= 0 || tolerance < 0)
        {
            return false;
        }

        var canonicalAspect = (double)CanonicalWidth / CanonicalHeight;
        var actualAspect = (double)width / height;
        var relativeDifference = Math.Abs(actualAspect - canonicalAspect) / canonicalAspect;

        if (relativeDifference > tolerance)
        {
            return false;
        }

        geometry = new ScreenGeometry(width, height);
        return true;
    }

    public PixelPoint Scale(PixelPoint canonicalPoint)
    {
        var x = (int)Math.Round(canonicalPoint.X * Width / (double)CanonicalWidth);
        var y = (int)Math.Round(canonicalPoint.Y * Height / (double)CanonicalHeight);
        return new PixelPoint(x, y);
    }

    public PixelRect Scale(PixelRect canonicalRect)
    {
        var topLeft = Scale(new PixelPoint(canonicalRect.X, canonicalRect.Y));
        var bottomRight = Scale(new PixelPoint(canonicalRect.Right, canonicalRect.Bottom));

        return new PixelRect(
            topLeft.X,
            topLeft.Y,
            Math.Max(1, bottomRight.X - topLeft.X),
            Math.Max(1, bottomRight.Y - topLeft.Y));
    }
}
