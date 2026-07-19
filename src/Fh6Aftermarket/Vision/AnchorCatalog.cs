using Fh6Aftermarket.Domain;

namespace Fh6Aftermarket.Vision;

public static class AnchorCatalog
{
    public static readonly PixelRect AftermarketMarkerSearch = new(0, 80, 1920, 870);

    public static readonly PixelPoint AftermarketMarkerExpectedCenter = new(355, 282);

    public static readonly PixelRect AftermarketLocationCard = new(420, 120, 460, 300);
}
