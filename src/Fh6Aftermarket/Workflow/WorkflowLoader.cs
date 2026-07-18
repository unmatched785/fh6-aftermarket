using System.Text.Json;

namespace Fh6Aftermarket.Workflow;

public static class WorkflowLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static WorkflowDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<WorkflowDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException("Workflow configuration is empty.");

        Validate(document);
        return document;
    }

    public static void Validate(WorkflowDocument document)
    {
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported workflow schema: {document.SchemaVersion}");
        }

        if (document.Flows.Count == 0)
        {
            throw new InvalidDataException("At least one workflow is required.");
        }

        var duplicate = document.Flows
            .GroupBy(flow => flow.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidDataException($"Duplicate workflow id: {duplicate.Key}");
        }

        foreach (var flow in document.Flows)
        {
            if (string.IsNullOrWhiteSpace(flow.Id) || flow.Steps.Count == 0)
            {
                throw new InvalidDataException("Every workflow needs an id and at least one step.");
            }

            foreach (var step in flow.Steps)
            {
                ValidateStep(flow.Id, step);
            }
        }
    }

    private static void ValidateStep(string flowId, WorkflowStep step)
    {
        switch (step.Kind.ToLowerInvariant())
        {
            case "key" when string.IsNullOrWhiteSpace(step.Key) || step.Repeat is null or < 1:
                throw new InvalidDataException($"Invalid key step in {flowId}: {step.Label}");
            case "wait" or "detect" when string.IsNullOrWhiteSpace(step.Anchor) || step.TimeoutMs is null or < 1:
                throw new InvalidDataException($"Invalid screen detection step in {flowId}: {step.Label}");
            case "mouse" when string.IsNullOrWhiteSpace(step.Target):
                throw new InvalidDataException($"Invalid mouse step in {flowId}: {step.Label}");
            case "key" or "wait" or "detect" or "mouse":
                break;
            default:
                throw new InvalidDataException($"Unknown step kind '{step.Kind}' in {flowId}.");
        }
    }
}
