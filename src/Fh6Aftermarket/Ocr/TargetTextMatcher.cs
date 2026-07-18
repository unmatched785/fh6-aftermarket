using System.Globalization;
using System.Text;

namespace Fh6Aftermarket.Ocr;

public sealed record TargetTextMatch(
    TargetVehicle Target,
    string Alias,
    double Score,
    bool Exact);

public sealed class TargetTextMatcher
{
    private const double MinimumFuzzyScore = 0.80;
    private readonly TargetCatalogDocument _catalog;

    public TargetTextMatcher(TargetCatalogDocument catalog)
    {
        _catalog = catalog;
    }

    public IReadOnlyList<TargetTextMatch> Match(string observedText)
    {
        var normalizedObserved = Normalize(observedText);
        if (normalizedObserved.Length == 0)
        {
            return [];
        }

        var results = new List<TargetTextMatch>();

        foreach (var target in _catalog.Targets)
        {
            TargetTextMatch? best = null;

            foreach (var alias in target.Aliases)
            {
                var normalizedAlias = Normalize(alias);
                if (normalizedAlias.Length == 0)
                {
                    continue;
                }

                TargetTextMatch? candidate;
                if (normalizedObserved.Contains(normalizedAlias, StringComparison.Ordinal))
                {
                    candidate = new TargetTextMatch(target, alias, 1, true);
                }
                else if (normalizedAlias.Length >= 7)
                {
                    var score = BestWindowScore(normalizedObserved, normalizedAlias);
                    candidate = score >= MinimumFuzzyScore
                        ? new TargetTextMatch(target, alias, score, false)
                        : null;
                }
                else
                {
                    candidate = null;
                }

                if (candidate is not null && (best is null || candidate.Score > best.Score))
                {
                    best = candidate;
                }
            }

            if (best is not null)
            {
                results.Add(best);
            }
        }

        return results
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Target.DisplayName, StringComparer.Ordinal)
            .ToArray();
    }

    public static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }

    private static double BestWindowScore(string observed, string target)
    {
        if (observed.Length == 0 || target.Length == 0)
        {
            return 0;
        }

        var minimumLength = Math.Max(1, target.Length - 2);
        var maximumLength = Math.Min(observed.Length, target.Length + 2);
        var best = 0d;

        for (var length = minimumLength; length <= maximumLength; length++)
        {
            for (var start = 0; start + length <= observed.Length; start++)
            {
                var window = observed.AsSpan(start, length);
                var distance = Levenshtein(window, target.AsSpan());
                var denominator = Math.Max(window.Length, target.Length);
                var score = 1 - distance / (double)denominator;
                best = Math.Max(best, score);
            }
        }

        return best;
    }

    private static int Levenshtein(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
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

        return previous[right.Length];
    }
}
