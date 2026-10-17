using System.Globalization;
using System.Text;
using Avala.Documentation.Contracts;

namespace Avala.Documentation.Features;

internal static class FeatureNames
{
    public const int SlugLength = 48;

    public const int TitleWords = 8;

    private static readonly HashSet<string> Quiet = ["the", "and", "for", "with", "then", "from", "into", "when", "that", "this", "are", "its"];

    public static string Slug(string title)
    {
        var slug = string.Empty;

        foreach (var word in Words(title))
        {
            var longer = slug.Length == 0 ? word : $"{slug}-{word}";

            if (longer.Length > SlugLength)
            {
                break;
            }

            slug = longer;
        }

        return slug.Length == 0 ? "feature" : slug;
    }

    public static FeatureSuggestion Suggest(string instruction, IReadOnlyList<Feature> features)
    {
        var named = Significant(instruction).ToHashSet();
        var closest = features
            .Select(feature => (Feature: feature, Title: Significant(feature.Title).Distinct().ToList()))
            .Where(scored => scored.Title.Count > 0)
            .Select(scored => (scored.Feature, Shared: scored.Title.Count(named.Contains), Of: scored.Title.Count))
            .Where(scored => scored.Shared * 2 > scored.Of)
            .OrderByDescending(scored => (double)scored.Shared / scored.Of)
            .ThenByDescending(scored => scored.Shared)
            .Select(scored => scored.Feature)
            .FirstOrDefault();

        return closest is null ? new FeatureSuggestion(New(instruction), true) : new FeatureSuggestion(closest, false);
    }

    public static Feature New(string instruction)
    {
        var clause = instruction.Split(['.', ',', ';', ':', '!', '?', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
        var words = clause.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(TitleWords).ToList();
        var title = words.Count == 0 ? "Feature" : string.Join(' ', words);

        return new Feature(Slug(title), char.ToUpperInvariant(title[0]) + title[1..]);
    }

    private static IEnumerable<string> Significant(string text) =>
        Words(text).Where(word => word.Length >= 3 && !Quiet.Contains(word));

    private static string[] Words(string text)
    {
        var plain = new StringBuilder();

        foreach (var letter in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark)
            {
                plain.Append(char.IsAsciiLetterOrDigit(letter) ? char.ToLowerInvariant(letter) : ' ');
            }
        }

        return plain.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
