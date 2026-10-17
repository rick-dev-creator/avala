using Avala.Documentation.Contracts;
using Avala.Sdk;

namespace Avala.Documentation.Features;

internal sealed record VersionChange(IReadOnlyList<C4Element> Added, IReadOnlyList<C4Element> Changed, IReadOnlyList<C4Element> Removed);

internal static class Storyboard
{
    public static C4Diagram Structure(VersionDocument document) =>
        document.Diagrams.FirstOrDefault(diagram => diagram.Level != C4Level.Dynamic)
        ?? new C4Diagram(C4Level.Container, document.Title, [], [], []);

    public static VersionChange Change(C4Diagram previous, C4Diagram current)
    {
        var before = previous.Elements.ToDictionary(element => element.Alias, StringComparer.Ordinal);
        var after = current.Elements.Select(element => element.Alias).ToHashSet(StringComparer.Ordinal);

        return new VersionChange(
            [.. current.Elements.Where(element => !before.ContainsKey(element.Alias))],
            [.. current.Elements.Where(element => before.TryGetValue(element.Alias, out var earlier) && !Same(earlier, element))],
            [.. previous.Elements.Where(element => !after.Contains(element.Alias))]);
    }

    public static PlayedVersion Play(int number, VersionKind kind, string file, VersionDocument document, Option<C4Diagram> previous)
    {
        var structure = Structure(document);
        var sections = document.Sections.ToDictionary(section => section.Anchor, StringComparer.Ordinal);
        var steps = previous.Match(
            earlier => Changes(Change(earlier, structure), sections),
            () => Elements(structure, sections));
        var flows = document.Diagrams.Where(diagram => diagram.Level == C4Level.Dynamic).Select(diagram => Flow(diagram, sections));
        var ordered = Ordered([.. steps, .. flows], document.Sections);

        return new PlayedVersion(number, kind, file, document, structure, Revealing(ordered, structure, previous))
        {
            Removed = previous.Match(earlier => Change(earlier, structure).Removed, () => []),
        };
    }

    private static IEnumerable<PlayStep> Elements(C4Diagram structure, Dictionary<string, DocSection> sections) =>
        structure.Elements.Select(element => Step(StepKind.Base, element, sections, element.Label));

    private static IEnumerable<PlayStep> Changes(VersionChange change, Dictionary<string, DocSection> sections) =>
    [
        .. change.Added.Select(element => Step(StepKind.Added, element, sections, element.Label)),
        .. change.Changed.Select(element => Step(StepKind.Changed, element, sections, element.Label)),
        .. change.Removed.Select(element => Step(StepKind.Removed, element, sections, element.Label)),
    ];

    private static PlayStep Step(StepKind kind, C4Element element, Dictionary<string, DocSection> sections, string fallback) =>
        sections.TryGetValue(element.Alias, out var section)
            ? new PlayStep(kind, section.Anchor, section.Title, section.Text) { Elements = [element.Alias] }
            : new PlayStep(kind, element.Alias, fallback, element.Description) { Elements = [element.Alias] };

    private static PlayStep Flow(C4Diagram diagram, Dictionary<string, DocSection> sections)
    {
        var anchor = FeatureNames.Slug(diagram.Title);
        var relations = diagram.Relations.OrderBy(relation => relation.Index.Match(index => index, () => int.MaxValue)).ToList();

        return sections.TryGetValue(anchor, out var section) || sections.TryGetValue($"flow-{anchor}", out section)
            ? new PlayStep(StepKind.Flow, section.Anchor, section.Title, section.Text) { Relations = relations }
            : new PlayStep(StepKind.Flow, anchor, diagram.Title, string.Empty) { Relations = relations };
    }

    private static List<PlayStep> Ordered(IReadOnlyList<PlayStep> steps, IReadOnlyList<DocSection> sections)
    {
        var order = sections.Select((section, index) => (section.Anchor, index)).ToDictionary(pair => pair.Anchor, pair => pair.index, StringComparer.Ordinal);

        return [.. steps.Select((step, index) => (step, index)).OrderBy(pair => order.TryGetValue(pair.step.Anchor, out var at) ? at : sections.Count + pair.index).Select(pair => pair.step)];
    }

    private static List<PlayStep> Revealing(List<PlayStep> steps, C4Diagram structure, Option<C4Diagram> previous)
    {
        var shown = previous.Match(earlier => earlier.Elements.Select(element => element.Alias).ToHashSet(StringComparer.Ordinal), () => new HashSet<string>(StringComparer.Ordinal));
        var drawn = previous.Match(earlier => earlier.Relations.Select(Key).ToHashSet(StringComparer.Ordinal), () => new HashSet<string>(StringComparer.Ordinal));
        var revealed = new List<PlayStep>();

        foreach (var step in steps)
        {
            if (step.Kind == StepKind.Flow)
            {
                revealed.Add(step);
                continue;
            }

            foreach (var alias in step.Elements)
            {
                if (step.Kind == StepKind.Removed)
                {
                    shown.Remove(alias);
                }
                else
                {
                    shown.Add(alias);
                }
            }

            var relations = structure.Relations.Where(relation => shown.Contains(relation.From) && shown.Contains(relation.To) && drawn.Add(Key(relation))).ToList();
            revealed.Add(step with { Relations = relations });
        }

        return revealed;
    }

    private static string Key(C4Relation relation) => $"{relation.From}>{relation.To}>{relation.Label}";

    private static bool Same(C4Element earlier, C4Element later) =>
        earlier.Label == later.Label && earlier.Shape == later.Shape && earlier.Technology == later.Technology && earlier.Description == later.Description;
}
