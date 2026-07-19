using System.Text.Json.Serialization;

namespace Fh6Aftermarket.Workflow;

public sealed record WorkflowDocument(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("flows")] IReadOnlyList<WorkflowFlow> Flows);

public sealed record WorkflowFlow(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("fromLanguage")] string FromLanguage,
    [property: JsonPropertyName("toLanguage")] string ToLanguage,
    [property: JsonPropertyName("automationReady")] bool AutomationReady,
    [property: JsonPropertyName("steps")] IReadOnlyList<WorkflowStep> Steps);

public sealed record WorkflowStep(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("key")] string? Key = null,
    [property: JsonPropertyName("repeat")] int? Repeat = null,
    [property: JsonPropertyName("anchor")] string? Anchor = null,
    [property: JsonPropertyName("timeoutMs")] int? TimeoutMs = null,
    [property: JsonPropertyName("target")] string? Target = null,
    [property: JsonPropertyName("maximumDurationMs")] int? MaximumDurationMs = null);
