using Avala.Sdk;

namespace Avala.Components.Graphs;

public enum RelayStretchKind
{
    Ran,
    Running,
    Waiting,
}

public enum RelayStopKind
{
    WindingDown,
    Capped,
    Compacting,
    Resumed,
    HandedOff,
}

public sealed record RelayStretch(int Lane, double From, double To, RelayStretchKind Kind);

public sealed record RelayStop(int Lane, double At, RelayStopKind Kind);

public sealed record RelayCourse(int Lanes, IReadOnlyList<RelayStretch> Stretches, IReadOnlyList<RelayStop> Stops)
{
    public static RelayCourse Empty { get; } = new(0, [], []);

    public bool IsLive => Stretches is [.., { Kind: RelayStretchKind.Running or RelayStretchKind.Waiting }];

    public bool Equivalent(RelayCourse other) =>
        Lanes == other.Lanes && Stretches.SequenceEqual(other.Stretches) && Stops.SequenceEqual(other.Stops);
}

public sealed record RelaySpacing(double Lane, double Inset, double Bend);

public enum RelayPieceKind
{
    Straight,
    Jump,
}

public sealed record RelayPiece(RelayPieceKind Kind, GraphPoint From, GraphPoint To, RelayStretchKind Stretch)
{
    public double Length => Kind == RelayPieceKind.Straight
        ? Math.Abs(To.X - From.X)
        : Math.Sqrt(Math.Pow(To.X - From.X, 2) + Math.Pow(To.Y - From.Y, 2)) * 1.15;

    public GraphPoint At(double fraction)
    {
        var t = Math.Clamp(fraction, 0, 1);

        if (Kind == RelayPieceKind.Straight)
        {
            return new GraphPoint(From.X + ((To.X - From.X) * t), From.Y);
        }

        var (first, second) = Handles();
        var u = 1 - t;

        return new GraphPoint(
            (u * u * u * From.X) + (3 * u * u * t * first.X) + (3 * u * t * t * second.X) + (t * t * t * To.X),
            (u * u * u * From.Y) + (3 * u * u * t * first.Y) + (3 * u * t * t * second.Y) + (t * t * t * To.Y));
    }

    public (GraphPoint First, GraphPoint Second) Handles()
    {
        var middle = (From.X + To.X) / 2;

        return (new GraphPoint(middle, From.Y), new GraphPoint(middle, To.Y));
    }
}

public sealed record RelayMarker(GraphPoint At, RelayStopKind Kind);

public sealed record RelayRoute(IReadOnlyList<RelayPiece> Pieces, IReadOnlyList<RelayMarker> Markers, IReadOnlyList<double> Lanes)
{
    public static RelayRoute Empty { get; } = new([], [], []);

    public double Length => Pieces.Sum(piece => piece.Length);

    public Option<GraphPoint> Baton => Pieces is [.., var last] ? last.To : Option<GraphPoint>.None;

    public Option<GraphPoint> Along(double fraction)
    {
        if (Pieces.Count == 0)
        {
            return Option<GraphPoint>.None;
        }

        var left = Math.Clamp(fraction, 0, 1) * Length;

        foreach (var piece in Pieces)
        {
            if (left <= piece.Length || piece == Pieces[^1])
            {
                return piece.At(piece.Length == 0 ? 1 : left / piece.Length);
            }

            left -= piece.Length;
        }

        return Pieces[^1].To;
    }
}

public static class RelayLayout
{
    public static RelayRoute Route(RelayCourse course, RelaySpacing spacing, double width)
    {
        if (course.Lanes == 0 || width <= 0)
        {
            return RelayRoute.Empty;
        }

        IReadOnlyList<double> lanes = [.. Enumerable.Range(0, course.Lanes).Select(lane => (lane * spacing.Lane) + (spacing.Lane / 2))];
        var span = Math.Max(0, width - (2 * spacing.Inset));
        double X(double at) => spacing.Inset + (Math.Clamp(at, 0, 1) * span);
        GraphPoint On(int lane, double at) => new(X(at), lanes[Math.Clamp(lane, 0, lanes.Count - 1)]);

        var pieces = new List<RelayPiece>();

        foreach (var stretch in course.Stretches)
        {
            var start = On(stretch.Lane, stretch.From);

            if (pieces is [.., var previous] && Math.Abs(previous.To.Y - start.Y) > 0.5)
            {
                var landing = new GraphPoint(start.X + Math.Min(spacing.Bend, (X(stretch.To) - start.X) / 2), start.Y);
                pieces.Add(new RelayPiece(RelayPieceKind.Jump, previous.To, landing, stretch.Kind));
                start = landing;
            }

            pieces.Add(new RelayPiece(RelayPieceKind.Straight, start, On(stretch.Lane, stretch.To), stretch.Kind));
        }

        return new RelayRoute(pieces, [.. course.Stops.Select(stop => new RelayMarker(Landed(pieces, stop, On(stop.Lane, stop.At)), stop.Kind))], lanes);
    }

    private static GraphPoint Landed(IReadOnlyList<RelayPiece> pieces, RelayStop stop, GraphPoint at) =>
        stop.Kind == RelayStopKind.HandedOff
        && pieces.FirstOrDefault(piece => piece.Kind == RelayPieceKind.Jump && Math.Abs(piece.To.Y - at.Y) < 0.5 && Math.Abs(piece.From.X - at.X) < 0.5) is { } jump
            ? jump.To
            : at;
}
