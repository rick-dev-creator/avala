namespace Avala.Components.Graphs;

internal sealed class Orbit(HubSpec hub, GraphPoint center, Drawing drawing, HubSpacing spacing)
{
    private const int ClearPushes = 24;

    private const int MaxPushes = 600;

    private const double TurnStep = 6;

    private const double Sweep = 60;

    private static readonly double[] Turns =
        [0, .. Enumerable.Range(1, (int)(Sweep / TurnStep)).SelectMany(step => new[] { step * TurnStep, -step * TurnStep })];

    private readonly GraphRect own = HubLayout.Square(center, hub.Radius);

    public List<GraphRect> Clear(IReadOnlyList<double> angles, double gap)
    {
        var mark = drawing.Mark();
        var rects = new List<GraphRect>(hub.Satellites.Count);

        foreach (var (size, preferred) in hub.Satellites.Zip(angles))
        {
            var found = Clear(size, preferred, gap).Take(1).ToList();

            if (found.Count == 0)
            {
                drawing.Rollback(mark);

                return [];
            }

            drawing.Add(found[0].Rect, found[0].Edge);
            rects.Add(found[0].Rect);
        }

        return rects;
    }

    public List<GraphRect> Apart(IReadOnlyList<double> angles)
    {
        var rects = new List<GraphRect>(hub.Satellites.Count);

        foreach (var (size, preferred) in hub.Satellites.Zip(angles))
        {
            var candidate = HubLayout.Candidate(hub, center, size, preferred, spacing.Gap);

            for (var push = 1; push < MaxPushes && drawing.Overlaps(candidate.Rect, spacing.Margin); push++)
            {
                candidate = HubLayout.Candidate(hub, center, size, preferred, spacing.Gap + (push * spacing.Step));
            }

            drawing.Add(candidate.Rect, candidate.Edge);
            rects.Add(candidate.Rect);
        }

        return rects;
    }

    private IEnumerable<(GraphRect Rect, GraphCurve Edge)> Clear(GraphSize size, double preferred, double gap) =>
        Enumerable.Range(0, ClearPushes)
            .SelectMany(push => Turns.Select(turn => HubLayout.Candidate(hub, center, size, preferred + turn, gap + (push * spacing.Step))))
            .Where(candidate => drawing.IsClear(candidate.Rect, candidate.Edge, own, spacing.Margin));
}
