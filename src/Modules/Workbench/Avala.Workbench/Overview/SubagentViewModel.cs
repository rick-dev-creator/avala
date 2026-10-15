using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Workbench.Board;
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

    public string Activity { get; } = Latest(entry.Output);

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

    private static string Latest(string output)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;

        return line.Length <= 80 ? line : $"{line[..80]}…";
    }
}
