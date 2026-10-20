using Avala.Agents.Contracts.Events;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Conversation;

internal interface IRunMember
{
    bool InRun { get; set; }
}

internal sealed class ToolRuns
{
    public const int Folded = 4;

    private Dictionary<ToolViewModel, ToolGroupViewModel> groups = [];

    public event EventHandler? Toggled;

    public IReadOnlyList<ITimelineItem> Fold(IReadOnlyList<(ITimelineEntry Entry, ITimelineItem Item)> shown)
    {
        var asked = shown.Select(pair => pair.Entry).OfType<PermissionEntry>()
            .Where(permission => permission.WentToHuman || permission.WentToParent)
            .Select(permission => EntryKeys.Item(permission.Turn, permission.Item))
            .ToHashSet(StringComparer.Ordinal);
        var joins = shown.Select(pair => Joins(pair.Item, asked.Contains(pair.Entry.Key))).ToList();
        var folded = new List<ITimelineItem>(shown.Count);
        var kept = new Dictionary<ToolViewModel, ToolGroupViewModel>();
        var index = 0;

        while (index < shown.Count)
        {
            var end = RunEnd(shown, joins, index);
            var run = shown.Skip(index).Take(end - index).Select(pair => pair.Item).ToList();
            var grouped = run.Count(item => item is ToolViewModel) >= Folded && run[0] is ToolViewModel;

            foreach (var member in run.OfType<IRunMember>())
            {
                member.InRun = grouped;
            }

            if (grouped)
            {
                var group = Group((ToolViewModel)run[0]);
                group.Show(Summary(run));
                kept[(ToolViewModel)run[0]] = group;
                folded.Add(group);
                folded.AddRange(group.IsExpanded ? run : []);
            }
            else
            {
                folded.AddRange(run);
            }

            index = end;
        }

        groups = kept;

        return folded;
    }

    private ToolGroupViewModel Group(ToolViewModel first)
    {
        if (groups.TryGetValue(first, out var known))
        {
            return known;
        }

        var group = new ToolGroupViewModel();
        group.Toggled += (_, _) => Toggled?.Invoke(this, EventArgs.Empty);

        return group;
    }

    private static ToolRun Summary(List<ITimelineItem> run)
    {
        var tools = run.OfType<ToolViewModel>().ToList();
        var running = tools.LastOrDefault(tool => tool.IsRunning);

        return new ToolRun(
            tools.GroupBy(tool => tool.Kind).OrderByDescending(kind => kind.Count()).First().Key,
            ConversationPhrases.Tools(tools.Select(tool => tool.Kind)),
            running is null ? string.Empty : Doing(running),
            string.Join('\n', run.OfType<ISearchable>().Select(item => item.SearchText)));
    }

    private static string Doing(ToolViewModel tool) =>
        tool.Kind == ItemKind.Command && tool.Input.Length > 0 ? tool.Input : tool.Title;

    private static Joining Joins(ITimelineItem item, bool asked) => item switch
    {
        ToolViewModel tool when !asked && !tool.StandsAlone => Joining.Tool,
        ReasoningViewModel => Joining.Between,
        _ => Joining.Never,
    };

    private static int RunEnd(IReadOnlyList<(ITimelineEntry Entry, ITimelineItem Item)> shown, List<Joining> joins, int start)
    {
        if (joins[start] != Joining.Tool)
        {
            return start + 1;
        }

        var last = start;

        for (var index = start + 1; index < shown.Count && joins[index] != Joining.Never; index++)
        {
            last = joins[index] == Joining.Tool ? index : last;
        }

        return last + 1;
    }

    private enum Joining
    {
        Never,
        Between,
        Tool,
    }
}
