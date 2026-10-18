using Avala.Agents.Contracts.Events;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Reviewing;

internal sealed record ReportedRun(string Command, bool Passed);

internal static class ReportedRuns
{
    public static IReadOnlyList<ReportedRun> AfterLastEdit(IEnumerable<ITimelineEntry> entries)
    {
        var tools = entries.OfType<ToolEntry>().Where(tool => !tool.Denied).ToList();
        var lastEdit = tools.FindLastIndex(tool => tool.Kind == ItemKind.FileEdit);

        return [.. tools.Skip(lastEdit + 1)
            .Where(tool => tool.Kind == ItemKind.Command && tool.Outcome.Match(outcome => outcome is ItemOutcome.Succeeded or ItemOutcome.Failed, () => false))
            .Select(tool => new ReportedRun(tool.Input.Match(input => input, () => tool.Title), tool.Outcome == ItemOutcome.Succeeded))
            .GroupBy(run => run.Command, StringComparer.Ordinal)
            .Select(runs => runs.Last())];
    }
}
