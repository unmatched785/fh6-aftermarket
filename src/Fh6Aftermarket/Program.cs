using System.Drawing;
using System.Drawing.Imaging;
using Fh6Aftermarket.Capture;
using Fh6Aftermarket.Ocr;
using Fh6Aftermarket.Vision;
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
Console.WriteLine("  --config <workflow.json> --validate");
Console.WriteLine("  --config <workflow.json> --print-flow <flow-id>");

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
