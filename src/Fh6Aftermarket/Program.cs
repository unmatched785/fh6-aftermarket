using System.Drawing;
using System.Drawing.Imaging;
using Fh6Aftermarket.Capture;
using Fh6Aftermarket.Input;
using Fh6Aftermarket.Ocr;
using Fh6Aftermarket.Safety;
using Fh6Aftermarket.Vision;
using Fh6Aftermarket.Watch;
using Fh6Aftermarket.Workflow;

if (args.Length == 2 && args[0] == "--inspect-image")
{
    using var image = new Bitmap(args[1]);
    PrintObservation(AftermarketScreenObserver.Observe(image));
    return;
}

if (args.Length == 2 && args[0] == "--capture-foreground")
{
    using var capture = ForegroundWindowCapture.Capture();
    var outputPath = Path.GetFullPath(args[1]);
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
        ?? throw new InvalidOperationException("Capture output directory is invalid."));
    capture.Image.Save(outputPath, ImageFormat.Png);
    Console.WriteLine($"Captured foreground window: {capture.Title}");
    Console.WriteLine($"Saved: {outputPath}");
    PrintObservation(AftermarketScreenObserver.Observe(capture.Image));
    return;
}

if (args.Length == 4 && args[0] == "--targets" && args[2] == "--match-text")
{
    var catalog = TargetCatalog.Load(args[1]);
    var matches = new TargetTextMatcher(catalog).Match(args[3]);
    Console.WriteLine($"Target matches: {matches.Count}");

    foreach (var match in matches)
    {
        Console.WriteLine(
            $"- {match.Target.DisplayName} via '{match.Alias}' " +
            $"score={match.Score:F3} exact={match.Exact}");
    }

    return;
}

if (args.Length is 4 or 6 && args[0] == "--analyze-aftermarket-image" && args[2] == "--targets")
{
    var tessdataPath = args.Length == 6 && args[4] == "--tessdata-dir"
        ? args[5]
        : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "scoop",
            "apps",
            "tesseract-languages",
            "current");

    using var image = new Bitmap(args[1]);
    var catalog = TargetCatalog.Load(args[3]);
    var analyzer = new AftermarketImageAnalyzer(
        new TesseractCliRecognizer("tesseract", tessdataPath),
        new TargetTextMatcher(catalog));
    PrintAftermarketScan(analyzer.Analyze(image));
    return;
}

if (args.Length >= 3 && args[0] == "--watch-foreground" && args[1] == "--targets")
{
    var tessdataPath = GetOption(args, "--tessdata-dir")
        ?? GetDefaultTessdataPath();
    var titleText = GetOption(args, "--title-contains") ?? "Forza";
    var intervalMilliseconds = GetIntegerOption(args, "--interval-ms", 1_000);
    var maxSamples = GetIntegerOption(args, "--max-samples", 120);

    var catalog = TargetCatalog.Load(args[2]);
    var analyzer = new AftermarketImageAnalyzer(
        new TesseractCliRecognizer("tesseract", tessdataPath),
        new TargetTextMatcher(catalog));
    var watcher = new ReadOnlyForegroundWatcher(
        analyzer.Analyze,
        new ReadOnlyWatchOptions(titleText, intervalMilliseconds, maxSamples),
        Console.WriteLine);

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    Console.WriteLine("Read-only foreground watch started. No keyboard or mouse input will be sent.");
    var watchResult = watcher.Run(cancellation.Token);
    Console.WriteLine($"Watch outcome: {watchResult.Outcome} after {watchResult.SamplesTaken} sample(s).");
    return;
}

if (args.Contains("--run-one-shot", StringComparer.Ordinal))
{
    const string acknowledgement = "FH6_ONE_SHOT";
    var suppliedAcknowledgement = GetRequiredOption(args, "--acknowledge-live-input");
    if (!string.Equals(suppliedAcknowledgement, acknowledgement, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Live input acknowledgement must be exactly: {acknowledgement}");
    }

    var workflow = WorkflowLoader.Load(GetRequiredOption(args, "--config"));
    var flowId = GetRequiredOption(args, "--flow");
    var flow = workflow.Flows.SingleOrDefault(item =>
        string.Equals(item.Id, flowId, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Unknown flow: {flowId}");
    var safety = SafetySettingsLoader.Load(GetRequiredOption(args, "--safety"));

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    Console.WriteLine($"Starting one-shot flow: {flow.Id}");
    Console.WriteLine(
        $"Exact foreground required: {safety.RequiredWindowTitle}; " +
        $"emergency stop: {safety.EmergencyStopKey}");

    var runner = new OneShotFlowRunner(
        new WindowsKeySender(),
        ReadForegroundWindowState,
        Thread.Sleep,
        Console.WriteLine);
    var result = runner.Run(flow, safety, cancellation.Token);
    Console.WriteLine(
        $"One-shot flow ended after {result.KeysSent} key(s). " +
        "No post-restart input was sent.");
    return;
}

if (args.Length == 3 && args[0] == "--config" && args[2] == "--validate")
{
    var document = WorkflowLoader.Load(args[1]);
    Console.WriteLine($"Validated {document.Flows.Count} workflow(s). Automation remains disabled.");
    return;
}

if (args.Length == 4 && args[0] == "--config" && args[2] == "--print-flow")
{
    PrintFlow(args[1], args[3]);
    return;
}

Console.WriteLine("FH6 Aftermarket Watcher - observer mode (input disabled)");
Console.WriteLine("Usage:");
Console.WriteLine("  --inspect-image <image-path>");
Console.WriteLine("  --capture-foreground <output.png>");
Console.WriteLine("  --targets <targets.json> --match-text <recognized-text>");
Console.WriteLine("  --analyze-aftermarket-image <image> --targets <targets.json> [--tessdata-dir <dir>]");
Console.WriteLine("  --watch-foreground --targets <targets.json> [--title-contains <text>] [--interval-ms <n>] [--max-samples <n>]");
Console.WriteLine("  --config <workflow.json> --validate");
Console.WriteLine("  --config <workflow.json> --print-flow <flow-id>");
Console.WriteLine("  --run-one-shot --config <workflow.json> --flow <id> --safety <safety.json> --acknowledge-live-input FH6_ONE_SHOT");

static void PrintFlow(string configPath, string flowId)
{
    var document = WorkflowLoader.Load(configPath);
    var flow = document.Flows.SingleOrDefault(
        item => string.Equals(item.Id, flowId, StringComparison.OrdinalIgnoreCase));

    if (flow is null)
    {
        throw new InvalidOperationException($"Unknown flow: {flowId}");
    }

    Console.WriteLine($"{flow.Id}: {flow.FromLanguage} -> {flow.ToLanguage}");
    for (var index = 0; index < flow.Steps.Count; index++)
    {
        var step = flow.Steps[index];
        var detail = step.Kind switch
        {
            "key" => $"{step.Key} x{step.Repeat}",
            "wait" or "detect" => $"{step.Anchor} (timeout {step.TimeoutMs} ms)",
            "mouse" => step.Target,
            _ => "unknown"
        };

        Console.WriteLine($"{index + 1,2}. {step.Kind,-6} {detail} - {step.Label}");
    }
}

static void PrintObservation(ScreenObservation observation)
{
    Console.WriteLine($"Resolution: {observation.Width}x{observation.Height}");
    Console.WriteLine($"Supported 16:9: {observation.SupportedAspectRatio}");
    Console.WriteLine($"Marker state: {observation.State}");
    Console.WriteLine($"Marker candidates: {observation.Candidates.Count}");
    Console.WriteLine($"Card bright coverage: {observation.CardBrightCoverage:P2}");

    for (var index = 0; index < observation.Candidates.Count; index++)
    {
        var candidate = observation.Candidates[index];
        Console.WriteLine(
            $"  #{index + 1}: center=({candidate.Center.X},{candidate.Center.Y}) " +
            $"bounds=({candidate.Bounds.X},{candidate.Bounds.Y}," +
            $"{candidate.Bounds.Width},{candidate.Bounds.Height}) " +
            $"purple={candidate.PurplePixelCount} " +
            $"sat={candidate.MeanSaturation:F3} value={candidate.MeanValue:F3}");
    }
}

static void PrintAftermarketScan(AftermarketScanResult result)
{
    Console.WriteLine($"Scan state: {result.State}");
    Console.WriteLine($"Selling banners: {result.Banners.Count}");
    Console.WriteLine($"Readable banners: {result.ReadableBannerCount}");
    Console.WriteLine($"Target banners: {result.TargetCount}");

    for (var index = 0; index < result.Banners.Count; index++)
    {
        var banner = result.Banners[index];
        Console.WriteLine(
            $"  #{index + 1}: line=({banner.Region.GreenLine.X},{banner.Region.GreenLine.Y}," +
            $"{banner.Region.GreenLine.Width},{banner.Region.GreenLine.Height}) " +
            $"text=({banner.Region.TextRegion.X},{banner.Region.TextRegion.Y}," +
            $"{banner.Region.TextRegion.Width},{banner.Region.TextRegion.Height})");
        Console.WriteLine($"      OCR: {banner.Recognition.CombinedText}");

        foreach (var match in banner.TargetMatches)
        {
            Console.WriteLine(
                $"      TARGET: {match.Target.DisplayName} via '{match.Alias}' " +
                $"score={match.Score:F3}");
        }
    }
}

static string GetDefaultTessdataPath()
{
    return Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "scoop",
        "apps",
        "tesseract-languages",
        "current");
}

static string? GetOption(string[] arguments, string name)
{
    var index = Array.FindIndex(arguments, value => value == name);
    if (index < 0)
    {
        return null;
    }

    if (index + 1 >= arguments.Length || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Missing value for {name}.");
    }

    return arguments[index + 1];
}

static string GetRequiredOption(string[] arguments, string name)
{
    return GetOption(arguments, name)
        ?? throw new InvalidOperationException($"Missing required option: {name}");
}

static ForegroundWindowState ReadForegroundWindowState()
{
    using var capture = ForegroundWindowCapture.Capture();
    return new ForegroundWindowState(
        capture.Title,
        capture.Image.Width,
        capture.Image.Height);
}

static int GetIntegerOption(string[] arguments, string name, int defaultValue)
{
    var text = GetOption(arguments, name);
    if (text is null)
    {
        return defaultValue;
    }

    return int.TryParse(text, out var value)
        ? value
        : throw new InvalidOperationException($"Invalid integer for {name}: {text}");
}
