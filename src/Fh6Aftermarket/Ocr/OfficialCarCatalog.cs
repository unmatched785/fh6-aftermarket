using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fh6Aftermarket.Ocr;

public sealed record OfficialCarCatalogDocument(
    [property: JsonPropertyName("sourceId")] string SourceId,
    [property: JsonPropertyName("sourceName")] string SourceName,
    [property: JsonPropertyName("fetchedAt")] string FetchedAt,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("cars")] IReadOnlyList<OfficialCar> Cars);

public sealed record OfficialCar(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("year")] string Year,
    [property: JsonPropertyName("make")] string Make,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("carName")] string CarName);

public static class OfficialCarCatalog
{
    public const int MinimumKnownCarCount = 627;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static OfficialCarCatalogDocument Load(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<OfficialCarCatalogDocument>(stream, JsonOptions)
            ?? throw new InvalidDataException("Official car catalog is empty.");

        Validate(document);
        return document;
    }

    public static void Validate(OfficialCarCatalogDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.SourceId) ||
            string.IsNullOrWhiteSpace(document.SourceName) ||
            string.IsNullOrWhiteSpace(document.FetchedAt))
        {
            throw new InvalidDataException("Official car catalog source metadata is incomplete.");
        }

        if (document.Cars.Count < MinimumKnownCarCount ||
            document.Count != document.Cars.Count)
        {
            throw new InvalidDataException(
                $"Official car catalog is stale or inconsistent: " +
                $"minimum={MinimumKnownCarCount}, declared={document.Count}, actual={document.Cars.Count}.");
        }

        var duplicateId = document.Cars
            .GroupBy(car => car.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new InvalidDataException($"Duplicate official car id: {duplicateId.Key}");
        }

        if (document.Cars.Any(car =>
                string.IsNullOrWhiteSpace(car.Id) ||
                string.IsNullOrWhiteSpace(car.Year) ||
                string.IsNullOrWhiteSpace(car.Make) ||
                string.IsNullOrWhiteSpace(car.Model) ||
                string.IsNullOrWhiteSpace(car.CarName)))
        {
            throw new InvalidDataException("Official car catalog contains an invalid car row.");
        }
    }
}
