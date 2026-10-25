using System.Buffers;
using System.Text;
using Avala.Sdk;

namespace Avala.Fleet.Facts;

internal static class FactRules
{
    public const int ModelLength = 100;

    public const int EffortLength = 20;

    public const long MaximumTokens = 1_000_000_000_000;

    public const decimal MaximumCost = 1_000_000m;

    public const int CostDecimals = 6;

    public static Option<string> Model(Option<string> model) => Text(model, ModelLength);

    public static Option<string> Effort(Option<string> effort) => Text(effort, EffortLength);

    public static Option<FactCost> Cost(decimal amount, string currency, string basis)
    {
        var rounded = decimal.Round(amount, CostDecimals, MidpointRounding.ToEven);

        return rounded is < 0 or > MaximumCost || currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper)
            ? Option<FactCost>.None
            : new FactCost(rounded, currency, basis);
    }

    public static Result<FactTokens, FleetError> Tokens(long input, long output, long cacheRead, long cacheWrite, long reasoning) =>
        new[] { input, output, cacheRead, cacheWrite, reasoning }.All(count => count is >= 0 and <= MaximumTokens)
            ? new FactTokens(input, output, cacheRead, cacheWrite, reasoning)
            : FleetError.TokensOutOfBounds;

    private static Option<string> Text(Option<string> text, int length) =>
        text.Bind(value => value.Trim() is { Length: > 0 } trimmed && trimmed.Length <= length && IsStorable(trimmed)
            ? Option<string>.Some(trimmed)
            : Option<string>.None);

    private static bool IsStorable(string text)
    {
        var remaining = text.AsSpan();

        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done || Rune.IsControl(rune))
            {
                return false;
            }

            remaining = remaining[consumed..];
        }

        return true;
    }
}
