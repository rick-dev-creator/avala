using System.Globalization;
using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Presenting;
using Avala.Workbench.Spending;
using Avala.Workbench.Timeline;
using Avala.Workbench.Usage;

namespace Avala.Workbench.Overview;

internal interface IRecentActionViewModel
{
    string Text { get; }

    DateTimeOffset Since { get; }

    bool HasSince { get; }
}

internal sealed record RecentActionViewModel(string Text, DateTimeOffset Since, bool HasSince) : IRecentActionViewModel;

internal sealed record ContextGlance(bool IsShown, string Left, double Used, bool IsNear, bool Compacted)
{
    public static ContextGlance None { get; } = new(false, string.Empty, 0, false, false);
}

internal static class AgentGlance
{
    public const int RecentShown = 3;

    public static ContextGlance Context(Option<BoardJob> job) =>
        ContextReading.Of(job).Match(
            reported =>
            {
                var used = ContextReading.Used(reported);

                return new ContextGlance(true, Amounts.Percent(1 - used), used, ContextReading.IsNear(used), reported.Compacted);
            },
            () => ContextGlance.None);

    public static IReadOnlyList<IRecentActionViewModel> Recent(Option<BoardJob> job) =>
        job.Match<IReadOnlyList<IRecentActionViewModel>>(
            found => [.. found.Transcript.Entries.OfType<ToolEntry>()
                .Where(tool => tool.Kind != ItemKind.Subagent && tool.Drafted.IsNone)
                .TakeLast(RecentShown)
                .Reverse()
                .Select(tool => new RecentActionViewModel(
                    Capitalized(CommandPhrases.Request(tool.Kind, tool.Title)),
                    tool.Started.Match(started => started, () => DateTimeOffset.MinValue),
                    tool.Started.IsSome))],
            () => []);

    public static long Tokens(JobSpend spend) =>
        spend.Usage.Match(usage => usage.Tokens.Input + usage.Tokens.Output + usage.Tokens.CacheRead + usage.Tokens.CacheWrite + usage.Tokens.Reasoning, () => 0L);

    public static string TokensText(long tokens) => tokens switch
    {
        0 => string.Empty,
        < 1_000 => string.Create(CultureInfo.InvariantCulture, $"{tokens} tokens"),
        < 1_000_000 => string.Create(CultureInfo.InvariantCulture, $"{tokens / 1_000d:0.#}k tokens"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{tokens / 1_000_000d:0.##}M tokens"),
    };

    public static bool SameRecent(IReadOnlyList<IRecentActionViewModel> shown, IReadOnlyList<IRecentActionViewModel> next) =>
        shown.Select(action => (action.Text, action.Since)).SequenceEqual(next.Select(action => (action.Text, action.Since)));

    private static string Capitalized(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
