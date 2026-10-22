using Avala.Components.Graphs;
using Avala.Documentation.Contracts;

namespace Avala.Workbench.Documenting;

internal enum NodeState
{
    Hidden,
    Shown,
    New,
    Changed,
    Removed,
}

internal sealed record SceneNode(C4Element Element, NodeState State)
{
    public bool Focus { get; init; }

    public bool Reached { get; init; }
}

internal sealed record Scene(IReadOnlyList<SceneNode> Nodes, IReadOnlyList<GraphLink> Links, IReadOnlyList<GraphFrame> Frames, string Caption)
{
    public static Scene Empty { get; } = new([], [], [], string.Empty);

    public IReadOnlyList<GraphLink> Structure { get; init; } = [];

    public IReadOnlyList<GraphLink> Flows { get; init; } = [];
}

internal static class C4Scene
{
    public static Scene Of(FeatureStory story, int version, int step, int hop)
    {
        if (version < 0 || version >= story.Versions.Count)
        {
            return Scene.Empty;
        }

        var played = story.Versions[version];
        var earlier = version > 0 ? story.Versions[version - 1].Structure : new C4Diagram(C4Level.Container, string.Empty, [], [], []);
        var elements = played.Structure.Elements.Concat(played.Removed).ToList();
        var states = elements.ToDictionary(element => element.Alias, element => earlier.Elements.Any(old => old.Alias == element.Alias) ? NodeState.Shown : NodeState.Hidden, StringComparer.Ordinal);
        var current = played.Structure.Relations.Select(Key).ToHashSet(StringComparer.Ordinal);
        var visible = earlier.Relations.Where(relation => current.Contains(Key(relation))).ToList();
        var fresh = Apply(played, step, version, states, visible);

        var now = step >= 0 && step < played.Steps.Count ? played.Steps[step] : null;
        var flow = now is { Kind: StepKind.Flow } ? now.Relations.Take(hop + 1).ToList() : [];
        var links = flow.Count > 0
            ? Numbered(flow)
            : visible.Select(relation => new GraphLink(relation.From, relation.To, relation.Label, Kind(relation, fresh, states, now))).ToList();
        var caption = flow.Count > 0 ? $"{flow.Count} · {flow[^1].Label}" : string.Empty;

        return new Scene(Nodes(elements, states, now, flow), links, [.. played.Structure.Boundaries.Select(boundary => new GraphFrame(boundary.Alias, boundary.Label))], caption)
        {
            Structure = [.. played.Structure.Relations.Concat(earlier.Relations).Select(relation => new GraphLink(relation.From, relation.To, relation.Label, EdgeKind.Quiet))],
            Flows = [.. played.Steps.Where(shown => shown.Kind == StepKind.Flow).SelectMany(shown => shown.Relations).Select(relation => new GraphLink(relation.From, relation.To, relation.Label, EdgeKind.Flowing) { Badge = "0" })],
        };
    }

    private static List<SceneNode> Nodes(List<C4Element> elements, Dictionary<string, NodeState> states, PlayStep? now, List<C4Relation> flow)
    {
        var reached = flow.SelectMany(relation => new[] { relation.From, relation.To }).ToHashSet(StringComparer.Ordinal);

        return [.. elements.Select(element => new SceneNode(element, states[element.Alias])
        {
            Focus = now is { Kind: not StepKind.Flow } && now.Elements.Contains(element.Alias),
            Reached = reached.Contains(element.Alias),
        })];
    }

    private static List<GraphLink> Numbered(List<C4Relation> flow) =>
        [.. flow.Select((relation, index) => new GraphLink(relation.From, relation.To, relation.Label, EdgeKind.Flowing) { Badge = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) })];

    private static List<C4Relation> Apply(PlayedVersion played, int step, int version, Dictionary<string, NodeState> states, List<C4Relation> visible)
    {
        var fresh = new List<C4Relation>();

        foreach (var shown in played.Steps.Take(step + 1))
        {
            foreach (var alias in shown.Elements.Where(states.ContainsKey))
            {
                states[alias] = Mark(shown.Kind, version);
            }

            if (shown.Kind != StepKind.Flow)
            {
                fresh = [.. shown.Relations];
                visible.AddRange(shown.Relations);
            }
        }

        return fresh;
    }

    private static NodeState Mark(StepKind kind, int version) => kind switch
    {
        StepKind.Added => NodeState.New,
        StepKind.Changed => NodeState.Changed,
        StepKind.Removed => NodeState.Removed,
        _ => version == 0 ? NodeState.Shown : NodeState.New,
    };

    private static EdgeKind Kind(C4Relation relation, List<C4Relation> fresh, Dictionary<string, NodeState> states, PlayStep? now) =>
        states.GetValueOrDefault(relation.From) == NodeState.Removed || states.GetValueOrDefault(relation.To) == NodeState.Removed ? EdgeKind.Waiting
        : now is not null && fresh.Contains(relation) ? EdgeKind.Flowing
        : EdgeKind.Quiet;

    private static string Key(C4Relation relation) => $"{relation.From}>{relation.To}>{relation.Label}";
}
