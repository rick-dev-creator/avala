using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Presenting;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Overview;

internal enum SubagentState
{
    Running,
    Waiting,
    Done,
    Failed,
}

internal interface ISubagentViewModel
{
    string Name { get; }

    string Activity { get; }

    string Meter { get; }

    SubagentState State { get; }

    string StateText { get; }
}

internal sealed class SubagentViewModel(ToolEntry entry, bool waiting) : ISubagentViewModel
{
    public const int Shown = 5;

    private const string Prefix = "Subagent: ";

    public string Name { get; } = entry.Title.StartsWith(Prefix, StringComparison.Ordinal) ? entry.Title[Prefix.Length..] : entry.Title;

    public SubagentState State { get; } = entry.Outcome.Match(
        outcome => outcome == ItemOutcome.Succeeded ? SubagentState.Done : SubagentState.Failed,
        () => waiting ? SubagentState.Waiting : SubagentState.Running);

    public string Activity { get; } = entry.Outcome.IsNone && entry.Doing.Length > 0 ? Shortened(entry.Doing) : Latest(entry.Output);

    public string Meter { get; } = Metered(entry);

    public string StateText => State switch
    {
        SubagentState.Running => "running",
        SubagentState.Waiting => "waiting",
        SubagentState.Done => "done",
        _ => "failed",
    };

    public static IReadOnlyList<ISubagentViewModel> Of(Option<BoardJob> job) =>
        job.Match<IReadOnlyList<ISubagentViewModel>>(
            found =>
            {
                var asking = found.PendingDecisions > 0;

                return
                [
                    .. found.Transcript.Entries.OfType<ToolEntry>()
                        .Where(tool => tool.Kind == ItemKind.Subagent)
                        .TakeLast(Shown)
                        .Select(tool => new SubagentViewModel(tool, asking))
                        .OrderBy(subagent => subagent.State is SubagentState.Done or SubagentState.Failed),
                ];
            },
            () => []);

    public static IReadOnlyList<ISubagentViewModel> Kept(IReadOnlyList<ISubagentViewModel> current, IReadOnlyList<ISubagentViewModel> next) =>
        current.Select(Seen).SequenceEqual(next.Select(Seen)) ? current : next;

    private static (string Name, SubagentState State, string Activity, string Meter) Seen(ISubagentViewModel subagent) => (subagent.Name, subagent.State, subagent.Activity, subagent.Meter);

    private static string Metered(ToolEntry entry) =>
        string.Join(
            " · ",
            new[]
            {
                entry.Steps > 0 ? OverviewPhrases.Count(entry.Steps, "tool use") : string.Empty,
                entry.Tokens > 0 ? Amounts.Tokens(entry.Tokens) : string.Empty,
            }.Where(part => part.Length > 0));

    private static string Latest(string output) =>
        Shortened(output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty);

    private static string Shortened(string line)
    {
        var flat = line.ReplaceLineEndings(" ").Trim();

        return flat.Length <= 80 ? flat : $"{flat[..80]}…";
    }
}
