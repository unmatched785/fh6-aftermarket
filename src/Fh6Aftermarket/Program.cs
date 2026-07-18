using Fh6Aftermarket.Workflow;

if (args.Length != 3 || args[0] != "--config" || args[2] is not "--validate")
{
    if (args.Length == 4 && args[0] == "--config" && args[2] == "--print-flow")
    {
        PrintFlow(args[1], args[3]);
        return;
    }

    Console.WriteLine("FH6 Aftermarket Watcher - specification mode (input disabled)");
    Console.WriteLine("Usage:");
    Console.WriteLine("  --config <workflow.json> --validate");
    Console.WriteLine("  --config <workflow.json> --print-flow <flow-id>");
    return;
}

var document = WorkflowLoader.Load(args[1]);
Console.WriteLine($"Validated {document.Flows.Count} workflow(s). Automation remains disabled.");

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
