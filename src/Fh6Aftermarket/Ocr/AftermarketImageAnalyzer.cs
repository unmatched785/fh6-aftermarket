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
    IReadOnlyList<TargetTextMatch> TargetMatches);

public sealed record AftermarketScanResult(
    AftermarketScanState State,
    IReadOnlyList<BannerOcrResult> Banners)
{
    public int ReadableBannerCount => Banners.Count(banner => banner.Recognition.HasReadableText);
    public int TargetCount => Banners.Count(banner => banner.TargetMatches.Count > 0);
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
        var regions = SellingBannerDetector.Find(image);
        var banners = regions
            .Select(region =>
            {
                var recognition = _recognizer.Recognize(image, region.TextRegion);
                var matches = _matcher.Match(recognition.CombinedText);
                return new BannerOcrResult(region, recognition, matches);
            })
            .ToArray();

        var state = banners.Any(banner => banner.TargetMatches.Count > 0)
            ? AftermarketScanState.TargetFound
            : banners.Length > 0 && banners.All(banner => banner.Recognition.HasReadableText)
                ? AftermarketScanState.Clear
                : AftermarketScanState.Uncertain;

        return new AftermarketScanResult(state, banners);
    }
}
