using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fh6Aftermarket.Ocr;

public sealed record TargetCatalogDocument(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("targets")] IReadOnlyList<TargetVehicle> Targets);

public sealed record TargetVehicle(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("year")] int Year,
    [property: JsonPropertyName("make")] string Make,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("aliases")] IReadOnlyList<string> Aliases)
{
    public string DisplayName => $"{Year} {Make} {Model}";
}

public static class TargetCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static TargetCatalogDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<TargetCatalogDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException("Target catalog is empty.");

        Validate(document);
        return document;
    }

    public static void Validate(TargetCatalogDocument document)
    {
        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException($"Unsupported target schema: {document.SchemaVersion}");
        }

        if (document.Targets.Count == 0)
        {
            throw new InvalidDataException("At least one target vehicle is required.");
        }

        var duplicateId = document.Targets
            .GroupBy(target => target.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateId is not null)
        {
            throw new InvalidDataException($"Duplicate target id: {duplicateId.Key}");
        }

        foreach (var target in document.Targets)
        {
            if (target.Year is < 1900 or > 2100 ||
                string.IsNullOrWhiteSpace(target.Id) ||
                string.IsNullOrWhiteSpace(target.Make) ||
                string.IsNullOrWhiteSpace(target.Model) ||
                target.Aliases.Count == 0 ||
                target.Aliases.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidDataException($"Invalid target vehicle: {target.Id}");
            }
        }
    }
}
