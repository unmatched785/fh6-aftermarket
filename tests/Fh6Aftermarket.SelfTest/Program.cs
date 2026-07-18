using Fh6Aftermarket.Domain;
using Fh6Aftermarket.Workflow;

var failures = new List<string>();

CheckGeometry(1920, 1080, new PixelPoint(355, 282), new PixelPoint(355, 282));
CheckGeometry(2560, 1440, new PixelPoint(355, 282), new PixelPoint(473, 376));
CheckGeometry(3840, 2160, new PixelPoint(355, 282), new PixelPoint(710, 564));

if (ScreenGeometry.TryCreate(3440, 1440, out _))
{
    failures.Add("Ultrawide resolution must be rejected.");
}

var repoRoot = FindRepoRoot();
var workflowPath = Path.Combine(repoRoot, "config", "workflow.json");
var workflow = WorkflowLoader.Load(workflowPath);

CheckFlow(workflow, "kor-to-eng", expectedStepCount: 9);
CheckFlow(workflow, "eng-to-kor", expectedStepCount: 9);
CheckFlow(workflow, "post-restart-to-filtered-map", expectedStepCount: 19);
CheckFlow(workflow, "open-aftermarket-location", expectedStepCount: 8);

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

void CheckFlow(WorkflowDocument document, string id, int expectedStepCount)
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
