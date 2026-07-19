using System.Drawing;

namespace Fh6Aftermarket.Ocr;

public enum AftermarketScanState
{
    TargetFound,
    Clear,
    Uncertain
}

public sealed record BannerOcrResult(
    SellingBannerRegion Region,
    OcrRecognition Recognition,
    IReadOnlyList<TargetTextMatch> TargetMatches,
    CarNameResolution CarResolution);

public sealed record AftermarketScanResult(
    AftermarketScanState State,
    IReadOnlyList<BannerOcrResult> Banners,
    int SaleIconCount = 0)
{
    public int ReadableBannerCount => Banners.Count(banner => banner.Recognition.HasReadableText);
    public int TargetCount => Banners.Count(banner => banner.TargetMatches.Count > 0);
    public bool HasUnmatchedSaleIcon => SaleIconCount > Banners.Count;
}

public sealed class AftermarketImageAnalyzer
{
    private readonly TesseractCliRecognizer _recognizer;
    private readonly TargetTextMatcher _matcher;

    public AftermarketImageAnalyzer(
        TesseractCliRecognizer recognizer,
        TargetTextMatcher matcher)
    {
        _recognizer = recognizer;
        _matcher = matcher;
    }

    public AftermarketScanResult Analyze(Bitmap image)
    {
        var detection = SellingBannerDetector.Inspect(image);
        var banners = detection.Banners
            .Select(region =>
            {
                var recognition = _recognizer.Recognize(image, region.TextRegion);
                var resolution = _matcher.ResolvePreferredCar(recognition);
                var matches = _matcher.Match(recognition);
                return new BannerOcrResult(region, recognition, matches, resolution);
            })
            .ToArray();

        var state = banners.Any(banner => banner.TargetMatches.Count > 0)
            ? AftermarketScanState.TargetFound
            : banners.Length > 0 &&
              !detection.HasUnmatchedSaleIcon &&
              banners.All(banner =>
                  banner.Recognition.HasReadableText &&
                  banner.CarResolution.IsKnown)
                ? AftermarketScanState.Clear
                : AftermarketScanState.Uncertain;

        return new AftermarketScanResult(state, banners, detection.SaleIconCount);
    }
}
