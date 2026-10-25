namespace Avala.Server.Activity.Domain;

internal sealed record TokenCounts(long Input, long Output, long CacheRead, long CacheWrite, long Reasoning)
{
    public const long Maximum = 1_000_000_000_000;

    public static Admission<TokenCounts> Create(long input, long output, long cacheRead, long cacheWrite, long reasoning) =>
        new[] { input, output, cacheRead, cacheWrite, reasoning }.All(count => count is >= 0 and <= Maximum)
            ? Admission.Admit(new TokenCounts(input, output, cacheRead, cacheWrite, reasoning))
            : Admission.Refuse<TokenCounts>($"token counts are between 0 and {Maximum}");
}
