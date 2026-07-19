using System.Drawing;

namespace Fh6Aftermarket.Ocr;

public sealed record AftermarketMapCardScanResult(
    AftermarketScanState State,
    AftermarketMapCardRegion? Region,
    OcrRecognition? Recognition,
    IReadOnlyList<TargetTextMatch> TargetMatches);

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
                []);
        }

        var recognition = _recognizer.Recognize(image, region.VehicleNameRegion);
        var matches = _matcher.Match(recognition.CombinedText);
        var state = matches.Count > 0
            ? AftermarketScanState.TargetFound
            : recognition.HasReadableText
                ? AftermarketScanState.Clear
                : AftermarketScanState.Uncertain;

        return new AftermarketMapCardScanResult(state, region, recognition, matches);
    }
}
