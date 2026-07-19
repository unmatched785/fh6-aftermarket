using System.Globalization;
using System.Text;

namespace Fh6Aftermarket.Ocr;

public sealed record OfficialCarMatch(
    OfficialCar Car,
    double Score,
    bool Exact);

public sealed record CarNameResolution(
    string ObservedText,
    IReadOnlyList<OfficialCarMatch> Candidates)
{
    public bool IsKnown => Candidates.Count > 0;

    public OfficialCarMatch? Best => Candidates.FirstOrDefault();
}

public sealed class CarNameNormalizer
{
    private const double MinimumCandidateScore = 0.80;
    private readonly IReadOnlyList<SearchEntry> _entries;
    private readonly IReadOnlySet<string> _carIds;

    public CarNameNormalizer(OfficialCarCatalogDocument catalog)
    {
        _entries = catalog.Cars
            .Select(car => new SearchEntry(car, CreateAliases(car)))
            .ToArray();
        _carIds = catalog.Cars
            .Select(car => car.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool ContainsCarId(string id) => _carIds.Contains(id);

    public CarNameResolution Resolve(string observedText)
    {
        var observedTokens = Tokenize(observedText);
        if (observedTokens.Count == 0)
        {
            return new CarNameResolution(observedText, []);
        }

        var candidates = _entries
            .Select(entry => Score(entry, observedTokens))
            .Where(match => match.Score >= MinimumCandidateScore)
            .OrderByDescending(match => match.Exact)
            .ThenByDescending(match => match.Score)
            .ThenBy(match => match.Car.CarName, StringComparer.Ordinal)
            .ToArray();

        return new CarNameResolution(observedText, candidates);
    }

    private static OfficialCarMatch Score(SearchEntry entry, IReadOnlyList<string> observedTokens)
    {
        var bestScore = 0d;
        var exact = false;
        var observedCompact = string.Concat(observedTokens);

        foreach (var alias in entry.Aliases)
        {
            var aliasCompact = string.Concat(alias);
            if (string.Equals(observedCompact, aliasCompact, StringComparison.Ordinal))
            {
                return new OfficialCarMatch(entry.Car, 1, true);
            }

            bestScore = Math.Max(
                bestScore,
                TokenCoverage(observedTokens, alias, entry.Car.Year));
        }

        return new OfficialCarMatch(entry.Car, bestScore, exact);
    }

    private static double TokenCoverage(
        IReadOnlyList<string> observed,
        IReadOnlyList<string> candidate,
        string carYear)
    {
        var used = new bool[candidate.Count];
        var totalWeight = observed.Sum(TokenWeight);
        if (totalWeight == 0)
        {
            return 0;
        }

        var matchedWeight = 0d;
        foreach (var token in observed)
        {
            var quality = YearMatchQuality(token, carYear);
            var bestIndex = -1;

            for (var index = 0; index < candidate.Count; index++)
            {
                if (used[index])
                {
                    continue;
                }

                var tokenQuality = TokenMatchQuality(token, candidate[index]);
                if (tokenQuality > quality)
                {
                    quality = tokenQuality;
                    bestIndex = index;
                }
            }

            if (bestIndex >= 0)
            {
                used[bestIndex] = true;
            }

            matchedWeight += TokenWeight(token) * quality;
        }

        return matchedWeight / totalWeight;
    }

    private static double YearMatchQuality(string token, string year)
    {
        if (token.Length == 4 && string.Equals(token, year, StringComparison.Ordinal))
        {
            return 1;
        }

        return token.Length == 2 &&
               year.Length == 4 &&
               string.Equals(token, year[2..], StringComparison.Ordinal)
            ? 1
            : 0;
    }

    private static double TokenMatchQuality(string observed, string candidate)
    {
        if (string.Equals(observed, candidate, StringComparison.Ordinal))
        {
            return 1;
        }

        if (observed.Length == 1 && candidate.StartsWith(observed, StringComparison.Ordinal))
        {
            return 0.75;
        }

        if (observed.Length >= 2 && candidate.StartsWith(observed, StringComparison.Ordinal))
        {
            return 0.94;
        }

        if (candidate.Length >= 3 && observed.StartsWith(candidate, StringComparison.Ordinal))
        {
            return 0.90;
        }

        if (observed.Length < 4 || candidate.Length < 4)
        {
            return 0;
        }

        var similarity = Similarity(observed, candidate);
        return similarity >= 0.75 ? similarity : 0;
    }

    private static double Similarity(string left, string right)
    {
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];
        for (var column = 0; column <= right.Length; column++)
        {
            previous[column] = column;
        }

        for (var row = 1; row <= left.Length; row++)
        {
            current[0] = row;
            for (var column = 1; column <= right.Length; column++)
            {
                var substitutionCost = left[row - 1] == right[column - 1] ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + substitutionCost);
            }

            (previous, current) = (current, previous);
        }

        return 1 - previous[right.Length] / (double)Math.Max(left.Length, right.Length);
    }

    private static IReadOnlyList<IReadOnlyList<string>> CreateAliases(OfficialCar car)
    {
        var make = Tokenize(car.Make);
        var model = Tokenize(car.Model);
        var withoutMake = StartsWith(model, make)
            ? model.Skip(make.Count).ToArray()
            : model.ToArray();

        return new[]
            {
                model,
                withoutMake,
                Tokenize(car.CarName)
            }
            .Where(alias => alias.Count > 0)
            .DistinctBy(alias => string.Join('\0', alias), StringComparer.Ordinal)
            .Cast<IReadOnlyList<string>>()
            .ToArray();
    }

    private static bool StartsWith(
        IReadOnlyList<string> value,
        IReadOnlyList<string> prefix)
    {
        if (prefix.Count == 0 || prefix.Count > value.Count)
        {
            return false;
        }

        for (var index = 0; index < prefix.Count; index++)
        {
            if (!string.Equals(value[index], prefix[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static IReadOnlyList<string> Tokenize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                current.Append(char.ToUpperInvariant(character));
                continue;
            }

            Flush();
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (current.Length == 0)
            {
                return;
            }

            tokens.Add(current.ToString());
            current.Clear();
        }
    }

    private static int TokenWeight(string token) => Math.Max(1, token.Length);

    private sealed record SearchEntry(
        OfficialCar Car,
        IReadOnlyList<IReadOnlyList<string>> Aliases);
}
