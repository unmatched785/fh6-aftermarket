using System.Drawing;
using Fh6Aftermarket.Domain;
using Fh6Aftermarket.Input;
using Fh6Aftermarket.Ocr;
using Fh6Aftermarket.Safety;
using Fh6Aftermarket.Vision;
using Fh6Aftermarket.Watch;
using Fh6Aftermarket.Workflow;
using Fh6Aftermarket.Capture;

var failures = new List<string>();

CheckGeometry(1920, 1080, new PixelPoint(355, 282), new PixelPoint(355, 282));
CheckGeometry(2560, 1440, new PixelPoint(355, 282), new PixelPoint(473, 376));
CheckGeometry(3840, 2160, new PixelPoint(355, 282), new PixelPoint(710, 564));

if (ScreenGeometry.TryCreate(3440, 1440, out _))
{
    failures.Add("Ultrawide resolution must be rejected.");
}

CheckSyntheticMarker();
CheckSyntheticSelectedCard();
CheckSyntheticSellingBanner();
CheckReadOnlyWatcherTitleGuard();
CheckReadOnlyWatcherTargetStop();

var repoRoot = FindRepoRoot();
var workflowPath = Path.Combine(repoRoot, "config", "workflow.json");
var workflow = WorkflowLoader.Load(workflowPath);

CheckFlow(workflow, "kor-to-eng", expectedStepCount: 10, expectedAutomationReady: true);
CheckFlow(workflow, "eng-to-kor", expectedStepCount: 9, expectedAutomationReady: false);
CheckFlow(workflow, "post-restart-to-filtered-map", expectedStepCount: 19, expectedAutomationReady: false);
CheckFlow(workflow, "open-aftermarket-location", expectedStepCount: 8, expectedAutomationReady: false);
CheckOneShotRunner(workflow);

var targetsPath = Path.Combine(repoRoot, "config", "targets.json");
var targets = TargetCatalog.Load(targetsPath);
var matcher = new TargetTextMatcher(targets);

CheckTarget("Aventador '12", "2012-lamborghini-aventador-lp700-4");
CheckTarget("Lambo Sesto", "2011-lamborghini-sesto-elemento");
CheckTarget("F8 Tributo '19", "2019-ferrari-f8-tributo");
CheckTarget("Diab1o GTR", "1999-lamborghini-diablo-gtr");
CheckNoTarget("Ferrari F12tdf");
CheckNoTarget("Urus '19");

var safetyPath = Path.Combine(repoRoot, "config", "safety.json");
var safetyJson = File.ReadAllText(safetyPath);

if (!safetyJson.Contains("\"automationEnabled\": false", StringComparison.Ordinal))
{
    failures.Add("Automation must be disabled in the initial safety config.");
}

if (failures.Count > 0)
{
    Console.Error.WriteLine("SELF-TEST FAILED");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("SELF-TEST PASSED");
Console.WriteLine("- FHD/QHD/4K normalization");
Console.WriteLine("- ultrawide rejection");
Console.WriteLine("- four workflow definitions");
Console.WriteLine("- automation disabled by default");
Console.WriteLine("- synthetic marker and selected-card detection");
Console.WriteLine("- synthetic selling-banner detection");
Console.WriteLine("- read-only watcher title guard and target stop");
Console.WriteLine("- six target vehicles, display aliases, and OCR-tolerant matching");
Console.WriteLine("- one-shot safety gate, exact foreground guard, and 15-key KOR-to-ENG plan");

void CheckGeometry(int width, int height, PixelPoint canonical, PixelPoint expected)
{
    if (!ScreenGeometry.TryCreate(width, height, out var geometry) || geometry is null)
    {
        failures.Add($"Expected {width}x{height} to be supported.");
        return;
    }

    var actual = geometry.Scale(canonical);
    if (actual != expected)
    {
        failures.Add($"Scale mismatch for {width}x{height}: expected {expected}, got {actual}.");
    }
}

void CheckSyntheticMarker()
{
    using var bitmap = new Bitmap(1920, 1080);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.Clear(Color.FromArgb(35, 70, 45));
        using var purple = new SolidBrush(Color.FromArgb(116, 52, 190));
        using var white = new SolidBrush(Color.White);
        graphics.FillEllipse(purple, 325, 252, 60, 60);
        graphics.FillEllipse(white, 342, 269, 26, 26);
    }

    var observation = AftermarketScreenObserver.Observe(bitmap);
    if (observation.Candidates.Count != 1 || observation.State != MarkerVisualState.Clear)
    {
        failures.Add(
            $"Synthetic marker expected one clear candidate, got " +
            $"{observation.Candidates.Count} / {observation.State}.");
    }
}

void CheckSyntheticSelectedCard()
{
    using var bitmap = new Bitmap(1920, 1080);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.Clear(Color.FromArgb(35, 70, 45));
        using var purple = new SolidBrush(Color.FromArgb(116, 52, 190));
        using var white = new SolidBrush(Color.White);
        graphics.FillEllipse(purple, 325, 252, 60, 60);
        graphics.FillEllipse(white, 342, 269, 26, 26);
        graphics.FillRectangle(white, 440, 145, 400, 180);
    }

    var observation = AftermarketScreenObserver.Observe(bitmap);
    if (observation.Candidates.Count != 1 || observation.State != MarkerVisualState.SelectedCard)
    {
        failures.Add(
            $"Synthetic selected card expected one selected candidate, got " +
            $"{observation.Candidates.Count} / {observation.State}.");
    }
}

void CheckSyntheticSellingBanner()
{
    using var bitmap = new Bitmap(1920, 1080);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.Clear(Color.FromArgb(35, 45, 55));
        using var green = new SolidBrush(Color.FromArgb(0, 160, 90));
        graphics.FillEllipse(green, 520, 150, 82, 82);
        graphics.FillRectangle(green, 620, 200, 330, 5);
    }

    var regions = SellingBannerDetector.Find(bitmap);
    if (regions.Count != 1 || regions[0].GreenLine.Width != 330)
    {
        failures.Add(
            $"Synthetic selling banner expected one 330px line, got " +
            $"{regions.Count} / {(regions.Count > 0 ? regions[0].GreenLine.Width : 0)}.");
    }
}

void CheckReadOnlyWatcherTitleGuard()
{
    var analyzerWasCalled = false;
    var watcher = new ReadOnlyForegroundWatcher(
        _ =>
        {
            analyzerWasCalled = true;
            return new AftermarketScanResult(AftermarketScanState.Uncertain, []);
        },
        new ReadOnlyWatchOptions("Forza", 250, 1),
        _ => { },
        () => new CapturedWindow("Different application", CreateSyntheticWindow()));

    var result = watcher.Run(CancellationToken.None);
    if (result.Outcome != ReadOnlyWatchOutcome.TimedOut || analyzerWasCalled)
    {
        failures.Add(
            $"Watcher title guard expected TimedOut without analysis, got " +
            $"{result.Outcome} / analyzerCalled={analyzerWasCalled}.");
    }
}

void CheckReadOnlyWatcherTargetStop()
{
    var watcher = new ReadOnlyForegroundWatcher(
        _ => new AftermarketScanResult(AftermarketScanState.TargetFound, []),
        new ReadOnlyWatchOptions("Forza", 250, 3),
        _ => { },
        () => new CapturedWindow("Forza Horizon", CreateSyntheticWindow()));

    var result = watcher.Run(CancellationToken.None);
    if (result.Outcome != ReadOnlyWatchOutcome.TargetFound || result.SamplesTaken != 1)
    {
        failures.Add(
            $"Watcher expected immediate TargetFound, got " +
            $"{result.Outcome} after {result.SamplesTaken} sample(s).");
    }
}

Bitmap CreateSyntheticWindow()
{
    var bitmap = new Bitmap(1920, 1080);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.Clear(Color.Black);
    return bitmap;
}

void CheckFlow(
    WorkflowDocument document,
    string id,
    int expectedStepCount,
    bool expectedAutomationReady)
{
    var flow = document.Flows.SingleOrDefault(
        item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

    if (flow is null)
    {
        failures.Add($"Missing workflow: {id}");
        return;
    }

    if (flow.Steps.Count != expectedStepCount)
    {
        failures.Add($"Workflow {id} expected {expectedStepCount} steps, got {flow.Steps.Count}.");
    }

    if (flow.AutomationReady != expectedAutomationReady)
    {
        failures.Add(
            $"Workflow {id} automationReady expected {expectedAutomationReady}, " +
            $"got {flow.AutomationReady}.");
    }
}

void CheckOneShotRunner(WorkflowDocument document)
{
    var flow = document.Flows.Single(item => item.Id == "kor-to-eng");
    var sender = new RecordingKeySender();
    var safety = new SafetySettings
    {
        AutomationEnabled = false,
        SupportedLanguages = ["kor", "eng"],
        AspectRatioTolerance = 0.005,
        EmergencyStopKey = "F2",
        RequiredWindowTitle = "Forza Horizon 6",
        InputDelayMilliseconds = 50,
        MaxKeysPerOneShot = 32
    };
    var runner = new OneShotFlowRunner(
        sender,
        () => new ForegroundWindowState("Forza Horizon 6", 2560, 1440),
        _ => { },
        _ => { });

    try
    {
        _ = runner.Run(flow, safety, CancellationToken.None);
        failures.Add("One-shot runner must refuse automationEnabled=false.");
    }
    catch (InvalidOperationException)
    {
        // Expected fail-closed behavior.
    }

    var result = runner.Run(
        flow,
        safety with { AutomationEnabled = true },
        CancellationToken.None);

    if (result.KeysSent != 15 || sender.Keys.Count != 15)
    {
        failures.Add(
            $"KOR-to-ENG one-shot expected 15 keys, got " +
            $"{result.KeysSent} / recorded={sender.Keys.Count}.");
    }

    var expectedTail = new[] { "Down", "Enter", "Down", "Enter" };
    if (!sender.Keys.TakeLast(expectedTail.Length).SequenceEqual(expectedTail))
    {
        failures.Add("KOR-to-ENG tail must select English US, then Yes, then restart.");
    }

    var wrongWindowSender = new RecordingKeySender();
    var wrongWindowRunner = new OneShotFlowRunner(
        wrongWindowSender,
        () => new ForegroundWindowState("Different application", 2560, 1440),
        _ => { },
        _ => { });

    try
    {
        _ = wrongWindowRunner.Run(
            flow,
            safety with { AutomationEnabled = true },
            CancellationToken.None);
        failures.Add("One-shot runner must refuse a different foreground window.");
    }
    catch (InvalidOperationException)
    {
        if (wrongWindowSender.Keys.Count != 0)
        {
            failures.Add("Foreground guard must stop before the first key.");
        }
    }

    var stoppedSender = new RecordingKeySender { EmergencyStopPressed = true };
    var stoppedRunner = new OneShotFlowRunner(
        stoppedSender,
        () => new ForegroundWindowState("Forza Horizon 6", 2560, 1440),
        _ => { },
        _ => { });

    try
    {
        _ = stoppedRunner.Run(
            flow,
            safety with { AutomationEnabled = true },
            CancellationToken.None);
        failures.Add("One-shot runner must honor F2 before the first key.");
    }
    catch (OperationCanceledException)
    {
        if (stoppedSender.Keys.Count != 0)
        {
            failures.Add("F2 emergency stop must stop before the first key.");
        }
    }
}

void CheckTarget(string text, string expectedId)
{
    var matches = matcher.Match(text);
    if (!matches.Any(match => match.Target.Id == expectedId))
    {
        failures.Add($"Expected target '{expectedId}' for text '{text}'.");
    }
}

void CheckNoTarget(string text)
{
    var matches = matcher.Match(text);
    if (matches.Count != 0)
    {
        failures.Add($"Expected no target for text '{text}', got {matches[0].Target.Id}.");
    }
}

static string FindRepoRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);

    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not locate repository root.");
}

sealed class RecordingKeySender : IKeySender
{
    public List<string> Keys { get; } = [];

    public bool EmergencyStopPressed { get; set; }

    public void Send(string key) => Keys.Add(key);

    public bool IsDown(string key) => EmergencyStopPressed;
}
