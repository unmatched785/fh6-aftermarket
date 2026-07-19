using System.Drawing;

namespace Fh6Aftermarket.Ocr;

public sealed record AftermarketMapCardScanResult(
    AftermarketScanState State,
    AftermarketMapCardRegion? Region,
    OcrRecognition? Recognition,
    IReadOnlyList<TargetTextMatch> TargetMatches,
    CarNameResolution CarResolution);

public sealed class AftermarketMapCardAnalyzer
{
    private readonly TesseractCliRecognizer _recognizer;
    private readonly TargetTextMatcher _matcher;

    public AftermarketMapCardAnalyzer(
        TesseractCliRecognizer recognizer,
        TargetTextMatcher matcher)
    {
        _recognizer = recognizer;
        _matcher = matcher;
    }

    public AftermarketMapCardScanResult Analyze(Bitmap image)
    {
        if (!AftermarketMapCardDetector.TryFind(image, out var region) || region is null)
        {
            return new AftermarketMapCardScanResult(
                AftermarketScanState.Uncertain,
                null,
                null,
                [],
                new CarNameResolution(string.Empty, []));
        }

        var recognition = _recognizer.Recognize(image, region.VehicleNameRegion);
        var resolution = _matcher.ResolvePreferredCar(recognition);
        var matches = _matcher.Match(recognition);
        var state = Classify(recognition, matches, resolution);

        return new AftermarketMapCardScanResult(
            state,
            region,
            recognition,
            matches,
            resolution);
    }

    public static AftermarketScanState Classify(
        OcrRecognition recognition,
        IReadOnlyList<TargetTextMatch> matches,
        CarNameResolution resolution)
        => matches.Count > 0
            ? AftermarketScanState.TargetFound
            : recognition.HasReadableText && resolution.IsKnown
                ? AftermarketScanState.Clear
                : AftermarketScanState.Uncertain;
}
