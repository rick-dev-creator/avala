using System.Globalization;
using Avala.ClaudeCode.Conversations;
using Avala.Sdk;

namespace Avala.ClaudeCode.Settings;

internal enum CompactChoice
{
    Small,
    Standard,
    Large,
    Full,
}

internal static class CompactWindows
{
    private const int Reserved = 33_000;

    public static int Tokens(CompactChoice choice) => choice switch
    {
        CompactChoice.Small => 150_000,
        CompactChoice.Large => 500_000,
        CompactChoice.Full => 1_000_000,
        _ => CommandLine.DefaultCompactAt,
    };

    public static Option<CompactChoice> Of(int tokens) =>
        Enum.GetValues<CompactChoice>().Where(choice => Tokens(choice) == tokens).Select(choice => Option<CompactChoice>.Some(choice)).FirstOrDefault();

    public static string Summary(int tokens, bool automatic) =>
        automatic
            ? string.Create(CultureInfo.InvariantCulture, $"Summarizes the conversation at about {(tokens - Reserved) / 1000}k tokens, so each message reads at most that much")
            : "Never summarizes: every message reads the whole conversation, however long";
}
