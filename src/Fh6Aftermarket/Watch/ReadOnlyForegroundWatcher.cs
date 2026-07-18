using Fh6Aftermarket.Capture;
using Fh6Aftermarket.Domain;
using Fh6Aftermarket.Ocr;

namespace Fh6Aftermarket.Watch;

public enum ReadOnlyWatchOutcome
{
    TargetFound,
    Clear,
    Uncertain,
    TimedOut,
    Cancelled
}

public sealed record ReadOnlyWatchOptions(
    string RequiredTitleText,
    int IntervalMilliseconds,
    int MaxSamples,
    int RequiredConsecutiveClearSamples = 2)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RequiredTitleText))
        {
            throw new InvalidOperationException("The required window-title text cannot be empty.");
        }

        if (IntervalMilliseconds is < 250 or > 60_000)
        {
            throw new InvalidOperationException("Watch interval must be between 250 and 60000 ms.");
        }

        if (MaxSamples is < 1 or > 10_000)
        {
            throw new InvalidOperationException("Maximum samples must be between 1 and 10000.");
        }

        if (RequiredConsecutiveClearSamples is < 2 or > 10)
        {
            throw new InvalidOperationException("Consecutive clear samples must be between 2 and 10.");
        }
    }
}

public sealed record ReadOnlyWatchResult(
    ReadOnlyWatchOutcome Outcome,
    int SamplesTaken,
    AftermarketScanResult? LastScan);

public sealed class ReadOnlyForegroundWatcher
{
    private readonly Func<System.Drawing.Bitmap, AftermarketScanResult> _analyze;
    private readonly Func<CapturedWindow> _capture;
    private readonly ReadOnlyWatchOptions _options;
    private readonly Action<string> _writeLine;

    public ReadOnlyForegroundWatcher(
        Func<System.Drawing.Bitmap, AftermarketScanResult> analyze,
        ReadOnlyWatchOptions options,
        Action<string> writeLine,
        Func<CapturedWindow>? capture = null)
    {
        _analyze = analyze;
        _capture = capture ?? ForegroundWindowCapture.Capture;
        _options = options;
        _writeLine = writeLine;
        _options.Validate();
    }

    public ReadOnlyWatchResult Run(CancellationToken cancellationToken)
    {
        var consecutiveClearSamples = 0;
        AftermarketScanResult? lastScan = null;

        for (var sample = 1; sample <= _options.MaxSamples; sample++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new ReadOnlyWatchResult(ReadOnlyWatchOutcome.Cancelled, sample - 1, lastScan);
            }

            try
            {
                using var capture = _capture();
                if (!capture.Title.Contains(_options.RequiredTitleText, StringComparison.OrdinalIgnoreCase))
                {
                    consecutiveClearSamples = 0;
                    _writeLine(
                        $"[{sample}/{_options.MaxSamples}] Waiting: foreground title does not contain " +
                        $"'{_options.RequiredTitleText}'.");
                    if (Wait(cancellationToken))
                    {
                        return new ReadOnlyWatchResult(ReadOnlyWatchOutcome.Cancelled, sample, lastScan);
                    }

                    continue;
                }

                if (!ScreenGeometry.TryCreate(capture.Image.Width, capture.Image.Height, out _))
                {
                    consecutiveClearSamples = 0;
                    _writeLine(
                        $"[{sample}/{_options.MaxSamples}] Waiting: unsupported client size " +
                        $"{capture.Image.Width}x{capture.Image.Height}.");
                    if (Wait(cancellationToken))
                    {
                        return new ReadOnlyWatchResult(ReadOnlyWatchOutcome.Cancelled, sample, lastScan);
                    }

                    continue;
                }

                lastScan = _analyze(capture.Image);
                _writeLine(
                    $"[{sample}/{_options.MaxSamples}] {lastScan.State}: " +
                    $"banners={lastScan.Banners.Count}, readable={lastScan.ReadableBannerCount}, " +
                    $"targets={lastScan.TargetCount}");

                if (lastScan.State == AftermarketScanState.TargetFound)
                {
                    foreach (var target in lastScan.Banners
                                 .SelectMany(banner => banner.TargetMatches)
                                 .Select(match => match.Target.DisplayName)
                                 .Distinct(StringComparer.Ordinal))
                    {
                        _writeLine($"TARGET FOUND: {target}");
                    }

                    return new ReadOnlyWatchResult(ReadOnlyWatchOutcome.TargetFound, sample, lastScan);
                }

                if (lastScan.Banners.Count > 0 && lastScan.State == AftermarketScanState.Uncertain)
                {
                    _writeLine("STOP: selling banners were visible but OCR was incomplete.");
                    return new ReadOnlyWatchResult(ReadOnlyWatchOutcome.Uncertain, sample, lastScan);
                }

                if (lastScan.State == AftermarketScanState.Clear)
                {
                    consecutiveClearSamples++;
                    if (consecutiveClearSamples >= _options.RequiredConsecutiveClearSamples)
                    {
                        return new ReadOnlyWatchResult(ReadOnlyWatchOutcome.Clear, sample, lastScan);
                    }
                }
                else
                {
                    consecutiveClearSamples = 0;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                consecutiveClearSamples = 0;
                _writeLine($"[{sample}/{_options.MaxSamples}] Waiting: {exception.Message}");
            }

            if (Wait(cancellationToken))
            {
                return new ReadOnlyWatchResult(ReadOnlyWatchOutcome.Cancelled, sample, lastScan);
            }
        }

        return new ReadOnlyWatchResult(ReadOnlyWatchOutcome.TimedOut, _options.MaxSamples, lastScan);
    }

    private bool Wait(CancellationToken cancellationToken)
    {
        return cancellationToken.WaitHandle.WaitOne(_options.IntervalMilliseconds);
    }
}
