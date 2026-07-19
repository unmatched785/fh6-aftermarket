using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fh6Aftermarket.Safety;

public sealed record SafetySettings
{
    [JsonPropertyName("automationEnabled")]
    public bool AutomationEnabled { get; init; }

    [JsonPropertyName("practicalStartEnabled")]
    public bool PracticalStartEnabled { get; init; }

    [JsonPropertyName("supportedLanguages")]
    public IReadOnlyList<string> SupportedLanguages { get; init; } = [];

    [JsonPropertyName("aspectRatioTolerance")]
    public double AspectRatioTolerance { get; init; } = 0.005;

    [JsonPropertyName("emergencyStopKey")]
    public string EmergencyStopKey { get; init; } = "F2";

    [JsonPropertyName("requiredWindowTitle")]
    public string RequiredWindowTitle { get; init; } = "Forza Horizon 6";

    [JsonPropertyName("inputDelayMilliseconds")]
    public int InputDelayMilliseconds { get; init; } = 300;

    [JsonPropertyName("maxKeysPerOneShot")]
    public int MaxKeysPerOneShot { get; init; } = 32;

    [JsonPropertyName("maxRecognitionRetriesPerPoint")]
    public int MaxRecognitionRetriesPerPoint { get; init; } = 3;

    [JsonPropertyName("maxDuplicateRetriesPerPoint")]
    public int MaxDuplicateRetriesPerPoint { get; init; } = 2;

    [JsonPropertyName("maxFullScanRetries")]
    public int MaxFullScanRetries { get; init; } = 2;

    [JsonPropertyName("onUncertainRecognition")]
    public string OnUncertainRecognition { get; init; } = "retry_then_pause";

    [JsonPropertyName("onDuplicateRecognition")]
    public string OnDuplicateRecognition { get; init; } = "retry_nearby_then_next_point";

    [JsonPropertyName("onFocusLost")]
    public string OnFocusLost { get; init; } = "pause";
}

public static class SafetySettingsLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static SafetySettings Load(string path)
    {
        using var stream = File.OpenRead(path);
        var settings = JsonSerializer.Deserialize<SafetySettings>(stream, JsonOptions)
            ?? throw new InvalidDataException("Safety configuration is empty.");

        Validate(settings);
        return settings;
    }

    public static void Validate(SafetySettings settings)
    {
        if (settings.SupportedLanguages.Count == 0)
        {
            throw new InvalidDataException("At least one supported language is required.");
        }

        if (settings.AspectRatioTolerance is < 0 or > 0.05)
        {
            throw new InvalidDataException("Aspect ratio tolerance is outside the safe range.");
        }

        if (string.IsNullOrWhiteSpace(settings.EmergencyStopKey) ||
            string.IsNullOrWhiteSpace(settings.RequiredWindowTitle))
        {
            throw new InvalidDataException("Emergency stop key and exact window title are required.");
        }

        if (settings.InputDelayMilliseconds is < 50 or > 5_000)
        {
            throw new InvalidDataException("Input delay must be between 50 and 5000 ms.");
        }

        if (settings.MaxKeysPerOneShot is < 1 or > 100)
        {
            throw new InvalidDataException("One-shot key limit must be between 1 and 100.");
        }

        if (settings.MaxRecognitionRetriesPerPoint is < 1 or > 10 ||
            settings.MaxDuplicateRetriesPerPoint is < 1 or > 10 ||
            settings.MaxFullScanRetries is < 1 or > 5)
        {
            throw new InvalidDataException("Recognition retry limits are outside the safe range.");
        }

        if (settings.OnUncertainRecognition != "retry_then_pause" ||
            settings.OnDuplicateRecognition != "retry_nearby_then_next_point" ||
            settings.OnFocusLost != "pause")
        {
            throw new InvalidDataException("Unsafe recognition or focus-loss policy.");
        }
    }
}
