using System.Drawing;
using Fh6Aftermarket.Domain;
using Fh6Aftermarket.Input;
using Fh6Aftermarket.Ocr;
using Fh6Aftermarket.Safety;
using Fh6Aftermarket.Vision;
using Fh6Aftermarket.Watch;
using Fh6Aftermarket.Workflow;
using Fh6Aftermarket.Capture;
using Fh6Aftermarket.Gui;

var failures = new List<string>();

CheckGeometry(1920, 1080, new PixelPoint(355, 282), new PixelPoint(355, 282));
CheckGeometry(2560, 1440, new PixelPoint(355, 282), new PixelPoint(473, 376));
CheckGeometry(3840, 2160, new PixelPoint(355, 282), new PixelPoint(710, 564));

if (ScreenGeometry.TryCreate(3440, 1440, out _))
{
    failures.Add("Ultrawide resolution must be rejected.");
}

CheckSyntheticMarker();
CheckSyntheticMovedMarker();
CheckSyntheticSelectedCard();
CheckSyntheticSellingBanner();
CheckSyntheticMapCard();
CheckSyntheticMapIconCluster();
CheckPauseMenuLanguageClassifier();
CheckGroupedTimingSettings();
CheckOcrReadabilityGuard();
CheckTesseractInstallationLocator();
CheckReadOnlyWatcherTitleGuard();
CheckReadOnlyWatcherTargetStop();

var repoRoot = FindRepoRoot();
var workflowPath = Path.Combine(repoRoot, "config", "workflow.json");
var workflow = WorkflowLoader.Load(workflowPath);

CheckFlow(workflow, "kor-to-eng", expectedStepCount: 10, expectedAutomationReady: true);
CheckFlow(workflow, "eng-to-kor", expectedStepCount: 10, expectedAutomationReady: false);
CheckFlow(workflow, "post-restart-to-filtered-map", expectedStepCount: 19, expectedAutomationReady: false);
CheckFlow(workflow, "open-aftermarket-location", expectedStepCount: 8, expectedAutomationReady: false);
CheckFlow(workflow, "inspect-aftermarket-cars-on-map", expectedStepCount: 18, expectedAutomationReady: false);
CheckOneShotRunner(workflow);

var targetsPath = Path.Combine(repoRoot, "config", "targets.json");
var targets = TargetCatalog.Load(targetsPath);
var officialCarsPath = Path.Combine(repoRoot, "config", "official-cars.json");
var officialCars = OfficialCarCatalog.Load(officialCarsPath);
var normalizer = new CarNameNormalizer(officialCars);
var matcher = new TargetTextMatcher(targets, normalizer);

if (targets.Targets.Count != 6)
{
    failures.Add($"Target catalog must contain exactly six vehicles, got {targets.Targets.Count}.");
}

if (officialCars.Count < 627)
{
    failures.Add($"FH6 Meta official car catalog is stale: expected at least 627, got {officialCars.Count}.");
}

CheckTarget("Aventador '12", "2012-lamborghini-aventador-lp700-4");
CheckTarget("Lambo Sesto", "2011-lamborghini-sesto-elemento");
CheckTarget("F8 Tributo '19", "2019-ferrari-f8-tributo");
CheckTarget("F8 Tributo = |", "2019-ferrari-f8-tributo");
CheckTarget("599XX Evo = |", "2012-ferrari-599xx-evolution");
CheckTarget("Diab1o GTR", "1999-lamborghini-diablo-gtr");
CheckNoTarget("Ferrari F12tdf");
CheckNoTarget("Urus '19");
CheckNoTarget("Aventador SVJ");
CheckKnownCar("LaFerrari = |", "2013-ferrari-laferrari", expectedTarget: false);
CheckKnownCar("Abarth 131", "1980-abarth-fiat-131", expectedTarget: false);
CheckKnownCar("MC12 Corsa '08", "2008-maserati-mc12-versione-corsa", expectedTarget: false);
CheckKnownCar("L. Countach '21", "2021-lamborghini-countach-lpi-800-4", expectedTarget: false);
CheckKnownCar("Ferrari Dino", "1969-ferrari-dino-246-gt", expectedTarget: false);
CheckKnownCar("Huracan Tecnica", "2022-lamborghini-huracan-tecnica", expectedTarget: false);
CheckKnownCar("Lambo Miura", "1967-lamborghini-miura-p400", expectedTarget: false);
CheckKnownCar("Ferrari 430 S$", "2007-ferrari-430-scuderia", expectedTarget: false);
CheckUnknownCar("| = ||");
CheckCardClassificationSafety();

foreach (var target in targets.Targets)
{
    CheckTarget(target.DisplayName, target.Id);
}

var safetyPath = Path.Combine(repoRoot, "config", "safety.json");
var safetyJson = File.ReadAllText(safetyPath);
var safetySettings = SafetySettingsLoader.Load(safetyPath);

if (!safetyJson.Contains("\"automationEnabled\": false", StringComparison.Ordinal) ||
    !safetyJson.Contains("\"practicalStartEnabled\": true", StringComparison.Ordinal))
{
    failures.Add(
        "The practical build must enable only the open-world start while keeping full automation locked.");
}

if (safetySettings.OnUncertainRecognition != "retry_then_pause" ||
    safetySettings.OnDuplicateRecognition != "retry_nearby_then_next_point" ||
    safetySettings.OnFocusLost != "pause")
{
    failures.Add("Recognition failures and focus loss must retry or pause, not hard-stop.");
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
Console.WriteLine("- five workflow definitions, including guarded map-card inspection");
Console.WriteLine("- open-world practical start enabled while full automation stays locked");
Console.WriteLine("- duplicate/OCR/focus failures retry or pause instead of hard-stop");
Console.WriteLine("- bundled, custom Scoop, Program Files, and override-guidance Tesseract discovery");
Console.WriteLine("- synthetic marker and selected-card detection");
Console.WriteLine("- synthetic selling-banner detection");
Console.WriteLine("- synthetic map-card header and vehicle-name region detection");
Console.WriteLine("- synthetic overlapping map-icon cluster detection");
Console.WriteLine("- read-only watcher title guard and target stop");
Console.WriteLine(
    $"- six target vehicles plus {officialCars.Count}-car FH6 Meta normalization and OCR-tolerant matching");
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
        DrawSyntheticGlobe(graphics, 329, 256);
    }

    var observation = AftermarketScreenObserver.Observe(bitmap);
    if (observation.Candidates.Count != 1 || observation.State != MarkerVisualState.Clear)
    {
        failures.Add(
            $"Synthetic marker expected one clear candidate, got " +
            $"{observation.Candidates.Count} / {observation.State}.");
    }
}

void CheckSyntheticMovedMarker()
{
    using var bitmap = new Bitmap(1920, 1080);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.Clear(Color.FromArgb(35, 70, 45));
        DrawSyntheticGlobe(graphics, 55, 616);

        using var white = new SolidBrush(Color.White);
        graphics.FillPolygon(white,
        [
            new Point(932, 515),
            new Point(983, 535),
            new Point(950, 556)
        ]);
    }

    var observation = AftermarketScreenObserver.Observe(bitmap);
    if (observation.Candidates.Count != 1 ||
        observation.State != MarkerVisualState.Clear ||
        Math.Abs(observation.Candidates[0].Center.X - 81) > 3 ||
        Math.Abs(observation.Candidates[0].Center.Y - 642) > 3)
    {
        failures.Add(
            $"Moved color-independent marker expected near (81,642), got " +
            $"{observation.Candidates.FirstOrDefault()?.Center} / {observation.State}.");
    }
}

void CheckSyntheticSelectedCard()
{
    using var bitmap = new Bitmap(1920, 1080);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.Clear(Color.FromArgb(35, 70, 45));
        using var white = new SolidBrush(Color.White);
        DrawSyntheticGlobe(graphics, 329, 256);
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

void DrawSyntheticGlobe(Graphics graphics, int x, int y)
{
    using var fill = new SolidBrush(Color.FromArgb(75, 75, 75));
    using var line = new Pen(Color.White, 4);
    graphics.FillEllipse(fill, x, y, 52, 52);
    graphics.DrawEllipse(line, x, y, 52, 52);
    graphics.DrawEllipse(line, x + 14, y, 24, 52);
    graphics.DrawLine(line, x + 2, y + 18, x + 50, y + 18);
    graphics.DrawLine(line, x + 2, y + 34, x + 50, y + 34);
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

    var detection = SellingBannerDetector.Inspect(bitmap);
    var regions = detection.Banners;
    if (regions.Count != 1 || regions[0].GreenLine.Width != 330 || detection.SaleIconCount != 1)
    {
        failures.Add(
            $"Synthetic selling banner expected one icon and one 330px line, got " +
            $"icons={detection.SaleIconCount}, regions={regions.Count}, " +
            $"width={(regions.Count > 0 ? regions[0].GreenLine.Width : 0)}.");
    }
}

void CheckOcrReadabilityGuard()
{
    var noisy = new OcrRecognition([
        new OcrAttempt(13, "Petit PY (eae |)", 48.6),
        new OcrAttempt(7, string.Empty, -1),
        new OcrAttempt(11, "ca", 61)
    ]);
    if (noisy.HasReadableText)
    {
        failures.Add("Low-confidence overlay text must not qualify as readable vehicle text.");
    }

    var vehicle = new OcrRecognition([
        new OcrAttempt(13, "Ferrari 812", 92.7),
        new OcrAttempt(7, string.Empty, -1),
        new OcrAttempt(11, string.Empty, -1)
    ]);
    if (!vehicle.HasReadableText)
    {
        failures.Add("High-confidence vehicle text must qualify as readable.");
    }

    var mixed = new OcrRecognition([
        new OcrAttempt(13, "MC12 Corsa '08", 88.4),
        new OcrAttempt(7, "MC12 Corsa '08", 91.2),
        new OcrAttempt(11, "MC12 Corsa '08 localized body noise", 97.5)
    ]);
    if (mixed.PreferredVehicleNameAttempt?.PageSegmentationMode != 7)
    {
        failures.Add("Single-line OCR must be preferred over a noisier sparse-text result.");
    }
}

void CheckSyntheticMapCard()
{
    using var bitmap = new Bitmap(1920, 1080);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.Clear(Color.FromArgb(40, 70, 45));
        using var blue = new SolidBrush(Color.FromArgb(30, 25, 165));
        using var white = new SolidBrush(Color.FromArgb(245, 245, 245));
        graphics.FillRectangle(blue, 540, 150, 420, 36);
        graphics.FillRectangle(white, 540, 186, 420, 210);
    }

    if (!AftermarketMapCardDetector.TryFind(bitmap, out var region) || region is null)
    {
        failures.Add("Synthetic map card was not detected.");
        return;
    }

    if (region.Header.Width != 420 ||
        region.VehicleNameRegion.X != 552 ||
        region.VehicleNameRegion.Y != 190 ||
        region.VehicleNameRegion.Height != 36)
    {
        failures.Add(
            $"Synthetic map card geometry mismatch: header={region.Header}, " +
            $"name={region.VehicleNameRegion}.");
    }
}

void CheckSyntheticMapIconCluster()
{
    using var bitmap = new Bitmap(1920, 1080);
    using (var graphics = Graphics.FromImage(bitmap))
    {
        graphics.Clear(Color.FromArgb(30, 65, 40));
        using var green = new SolidBrush(Color.FromArgb(0, 175, 105));
        graphics.FillEllipse(green, 700, 400, 50, 50);
        graphics.FillRectangle(green, 675, 425, 60, 35);
        graphics.FillEllipse(green, 715, 415, 50, 50);
        graphics.FillRectangle(green, 690, 440, 60, 35);
        graphics.FillEllipse(green, 730, 430, 50, 50);
        graphics.FillRectangle(green, 705, 455, 60, 35);
    }

    var observation = AftermarketMapIconClusterDetector.Inspect(bitmap);
    if (!observation.HasSingleCluster)
    {
        failures.Add(
            $"Synthetic map icon cluster expected one candidate, got " +
            $"{observation.Candidates.Count}.");
        return;
    }

    var cluster = observation.Candidates[0];
    if (cluster.HoverTargets.Count != 3 ||
        cluster.HoverTargets.Distinct().Count() != 3)
    {
        failures.Add("Synthetic map icon cluster must provide three distinct hover targets.");
    }

    var primary = cluster.HoverTargets[0];
    if (primary.X <= cluster.Bounds.Left + cluster.Bounds.Width / 2 ||
        primary.Y >= cluster.Bounds.Top + cluster.Bounds.Height / 2)
    {
        failures.Add("Primary map hover target must start on the 1-o'clock side of the cluster.");
    }

    if (cluster.HoverTargets[2].Y >= primary.Y)
    {
        failures.Add("Third map hover target must enter above the first two icon layers.");
    }
}

void CheckTesseractInstallationLocator()
{
    var root = Path.Combine(
        Path.GetTempPath(),
        $"fh6-aftermarket-tesseract-locator-{Guid.NewGuid():N}");
    var appDirectory = Path.Combine(root, "app");
    var executablePath = Path.Combine(appDirectory, "tesseract", "tesseract.exe");
    var tessdataPath = Path.Combine(appDirectory, "tessdata");

    try
    {
        Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
        Directory.CreateDirectory(tessdataPath);
        File.WriteAllText(executablePath, "test executable placeholder");
        File.WriteAllText(Path.Combine(tessdataPath, "eng.traineddata"), "test");
        File.WriteAllText(Path.Combine(tessdataPath, "kor.traineddata"), "test");

        var installation = TesseractInstallationLocator.Locate(
            new TesseractSearchContext(AppBaseDirectory: appDirectory));
        if (!string.Equals(
                installation.ExecutablePath,
                Path.GetFullPath(executablePath),
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                installation.TessdataPath,
                Path.GetFullPath(tessdataPath),
                StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Bundled Tesseract executable and tessdata paths must be discovered first.");
        }

        var scoopRoot = Path.Combine(root, "custom-scoop");
        var scoopExecutable = Path.Combine(
            scoopRoot,
            "apps",
            "tesseract",
            "current",
            "tesseract.exe");
        var scoopTessdata = Path.Combine(
            scoopRoot,
            "apps",
            "tesseract-languages",
            "current");
        WriteFakeInstallation(scoopExecutable, scoopTessdata);
        var scoopInstallation = TesseractInstallationLocator.Locate(
            new TesseractSearchContext(
                AppBaseDirectory: Path.Combine(root, "missing-app"),
                ScoopRoot: scoopRoot));
        if (!string.Equals(
                scoopInstallation.ExecutablePath,
                Path.GetFullPath(scoopExecutable),
                StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("A custom SCOOP root must be used instead of assuming the user-profile path.");
        }

        var programFiles = Path.Combine(root, "program-files");
        var programExecutable = Path.Combine(programFiles, "Tesseract-OCR", "tesseract.exe");
        var programTessdata = Path.Combine(programFiles, "Tesseract-OCR", "tessdata");
        WriteFakeInstallation(programExecutable, programTessdata);
        var programInstallation = TesseractInstallationLocator.Locate(
            new TesseractSearchContext(
                AppBaseDirectory: Path.Combine(root, "missing-app-2"),
                ProgramFiles: programFiles));
        if (!string.Equals(
                programInstallation.ExecutablePath,
                Path.GetFullPath(programExecutable),
                StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("A standard Program Files Tesseract installation must be discovered.");
        }

        try
        {
            _ = TesseractInstallationLocator.Locate(
                new TesseractSearchContext(
                    AppBaseDirectory: Path.Combine(root, "missing"),
                    ExecutableOverride: Path.Combine(root, "missing-tesseract.exe"),
                    TessdataOverride: Path.Combine(root, "missing-tessdata")));
            failures.Add("Missing explicit Tesseract paths must fail with setup guidance.");
        }
        catch (FileNotFoundException exception)
        {
            if (!exception.Message.Contains("FH6_TESSERACT_EXE", StringComparison.Ordinal) ||
                !exception.Message.Contains("FH6_TESSDATA_DIR", StringComparison.Ordinal))
            {
                failures.Add("Missing Tesseract guidance must name both override environment variables.");
            }
        }
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    void WriteFakeInstallation(string executable, string languageDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        Directory.CreateDirectory(languageDirectory);
        File.WriteAllText(executable, "test executable placeholder");
        File.WriteAllText(Path.Combine(languageDirectory, "eng.traineddata"), "test");
        File.WriteAllText(Path.Combine(languageDirectory, "kor.traineddata"), "test");
    }
}

void CheckPauseMenuLanguageClassifier()
{
    var english = PauseMenuLanguageDetector.Classify(
        "CAMPAIGN CARS MY HORIZON World Map Festival Playlist Settings Exit Game");
    if (english.Language != GameLanguage.English)
    {
        failures.Add(
            $"English pause menu expected English, got {english.Language} " +
            $"({english.EnglishScore}/{english.KoreanScore}).");
    }

    var korean = PauseMenuLanguageDetector.Classify(
        "캠페인 차량 나의 호라이즌 월드 맵 페스티벌 플레이리스트 설정 게임 종료");
    if (korean.Language != GameLanguage.Korean)
    {
        failures.Add(
            $"Korean pause menu expected Korean, got {korean.Language} " +
            $"({korean.EnglishScore}/{korean.KoreanScore}).");
    }

    var singleKoreanSignal = PauseMenuLanguageDetector.Classify("캠페인");
    if (singleKoreanSignal.Language != GameLanguage.Korean)
    {
        failures.Add("One unambiguous Korean menu signal must determine Korean.");
    }

    var singleEnglishSignal = PauseMenuLanguageDetector.Classify("CAMPAIGN");
    if (singleEnglishSignal.Language != GameLanguage.English)
    {
        failures.Add("One unambiguous English menu signal must determine English.");
    }

    var koreanScriptFallback = PauseMenuLanguageDetector.Classify("한글 문구가 여러 개 보임");
    if (koreanScriptFallback.Language != GameLanguage.Korean)
    {
        failures.Add("Hangul-dominant OCR must fall back to Korean.");
    }
}

void CheckGroupedTimingSettings()
{
    var timing = new AutomationTimingSettings
    {
        InputDelayMilliseconds = 500,
        TransitionDelayMilliseconds = 1_800,
        FastTravelLoadingMilliseconds = 8_000,
        RestartLoadingMilliseconds = 40_000,
        PostRestartFirstDelayMilliseconds = 15_000,
        PostRestartSecondDelayMilliseconds = 25_000,
        OpenWorldMapDelayMilliseconds = 9_000
    };

    AutomationTimingSettings.Validate(timing);
    if (timing.RepeatedKeyDelayMilliseconds != 50 ||
        timing.StartDelayMilliseconds != 1_800 ||
        timing.PointerSettleMilliseconds != 250 ||
        timing.PauseMenuSettleMilliseconds != 600 ||
        timing.MapZoomDurationMilliseconds != 1_800 ||
        timing.OpenWorldMapDelayMilliseconds != 9_000 ||
        timing.VehicleCardSettleMilliseconds != 600 ||
        timing.PostRestartFirstDelayMilliseconds != 15_000 ||
        timing.PostRestartSecondDelayMilliseconds != 25_000)
    {
        failures.Add("Grouped GUI timing values must derive uniform bounded delays.");
    }

    var defaults = new AutomationTimingSettings();
    if (defaults.InputDelayMilliseconds != 850 ||
        defaults.TransitionDelayMilliseconds != 2_500 ||
        defaults.FastTravelLoadingMilliseconds != 15_000 ||
        defaults.RestartLoadingMilliseconds != 60_000 ||
        defaults.PostRestartFirstDelayMilliseconds != 15_000 ||
        defaults.PostRestartSecondDelayMilliseconds != 30_000 ||
        defaults.OpenWorldMapDelayMilliseconds != 10_000)
    {
        failures.Add("Default GUI timing profile must stay on the conservative baseline.");
    }

    var slowerMachine = new AutomationTimingSettings
    {
        InputDelayMilliseconds = 2_000,
        TransitionDelayMilliseconds = 6_000
    };
    if (slowerMachine.PointerSettleMilliseconds != 1_000 ||
        slowerMachine.StartDelayMilliseconds != 2_000 ||
        slowerMachine.PauseMenuSettleMilliseconds != 2_000 ||
        slowerMachine.MapZoomDurationMilliseconds != 6_000 ||
        slowerMachine.VehicleCardSettleMilliseconds != 2_000)
    {
        failures.Add("Higher GUI timing values must not be truncated by hidden upper caps.");
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

void CheckKnownCar(string text, string expectedId, bool expectedTarget)
{
    var resolution = normalizer.Resolve(text);
    if (!resolution.Candidates.Any(candidate => candidate.Car.Id == expectedId))
    {
        failures.Add($"Expected official car '{expectedId}' for text '{text}'.");
    }

    var isTarget = matcher.Match(text, resolution).Count > 0;
    if (isTarget != expectedTarget)
    {
        failures.Add(
            $"Expected target={expectedTarget} after normalizing '{text}', got target={isTarget}.");
    }
}

void CheckUnknownCar(string text)
{
    var resolution = normalizer.Resolve(text);
    if (resolution.IsKnown)
    {
        failures.Add(
            $"Expected unresolved official car text '{text}', got {resolution.Candidates[0].Car.Id}.");
    }
}

void CheckCardClassificationSafety()
{
    var repeatedNonTarget = new OcrRecognition([
        new OcrAttempt(13, "LaFerrari = |", 94),
        new OcrAttempt(7, "LaFerrari = |", 96),
        new OcrAttempt(11, "LaFerrari = |", 91)
    ]);
    CheckRecognitionState(
        repeatedNonTarget,
        AftermarketScanState.Clear,
        "Repeated LaFerrari OCR must normalize to a known non-target car.");

    var secondaryTargetSignal = new OcrRecognition([
        new OcrAttempt(13, "F8 Tributo = |", 82),
        new OcrAttempt(7, "LaFerrari = |", 96),
        new OcrAttempt(11, string.Empty, -1)
    ]);
    CheckRecognitionState(
        secondaryTargetSignal,
        AftermarketScanState.TargetFound,
        "A target signal from any OCR mode must override a preferred non-target reading.");

    var unknownReadableText = new OcrRecognition([
        new OcrAttempt(13, string.Empty, -1),
        new OcrAttempt(7, "Totally Unknown Car", 96),
        new OcrAttempt(11, string.Empty, -1)
    ]);
    CheckRecognitionState(
        unknownReadableText,
        AftermarketScanState.Uncertain,
        "Readable text outside the official DB must remain uncertain and must not clear a cycle.");
}

void CheckRecognitionState(
    OcrRecognition recognition,
    AftermarketScanState expected,
    string failureMessage)
{
    var resolution = matcher.ResolvePreferredCar(recognition);
    var matches = matcher.Match(recognition);
    var actual = AftermarketMapCardAnalyzer.Classify(recognition, matches, resolution);
    if (actual != expected)
    {
        failures.Add($"{failureMessage} Expected {expected}, got {actual}.");
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

    public void Hold(string key, int milliseconds) => Keys.Add(key);

    public void KeyDown(string key) => Keys.Add($"{key}:down");

    public void KeyUp(string key) => Keys.Add($"{key}:up");

    public bool IsDown(string key) => EmergencyStopPressed;
}
