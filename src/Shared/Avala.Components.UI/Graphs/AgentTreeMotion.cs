using Avala.Components.Graphs;
using Avalonia.Animation.Easings;

namespace Avala.Components.UI.Graphs;

internal sealed class AgentTreeMotion
{
    public static readonly TimeSpan Glide = TimeSpan.FromMilliseconds(240);

    public static readonly TimeSpan Grow = TimeSpan.FromMilliseconds(220);

    public static readonly TimeSpan FadeDelay = TimeSpan.FromMilliseconds(140);

    public static readonly TimeSpan Fade = TimeSpan.FromMilliseconds(260);

    public static readonly TimeSpan Collapse = TimeSpan.FromMilliseconds(220);

    public const double Rise = 6;

    private const double Shrink = 0.2;

    private const double Pull = 0.6;

    private static readonly SplineEasing Gentle = new(0.22, 1, 0.36, 1);
    private static readonly SplineEasing Drawing = new(0.4, 0, 0.2, 1);
    private static readonly SplineEasing Leaving = new(0.4, 0, 1, 1);

    private static readonly object Nowhere = new();

    private readonly Dictionary<object, Track> tracks = [];
    private readonly HashSet<object> present = [];
    private readonly List<Ghost> ghosts = [];
    private object[] keys = [];
    private IReadOnlyList<int> parents = [];

    public IReadOnlyList<Ghost> Ghosts => ghosts;

    public bool IsActive { get; private set; }

    public bool HasShown => tracks.Count > 0;

    public Track Of(object key, GraphRect target) => tracks.TryGetValue(key, out var track) ? track : new Track(target, false);

    public void Retarget(object[] next, IReadOnlyList<GraphRect> targets, IReadOnlyList<int> nextParents, bool animate)
    {
        Arrive(next, targets, animate);
        Leave(animate);

        if (!animate)
        {
            Snap();
        }

        keys = next;
        parents = nextParents;
        IsActive = animate && (ghosts.Count > 0 || tracks.Values.Any(track => track.IsActive));
    }

    private void Arrive(object[] next, IReadOnlyList<GraphRect> targets, bool animate)
    {
        var arrives = animate && tracks.Count > 0;
        present.Clear();

        for (var index = 0; index < next.Length && index < targets.Count; index++)
        {
            present.Add(next[index]);

            if (tracks.TryGetValue(next[index], out var track))
            {
                track.MoveTo(targets[index], animate);
            }
            else
            {
                tracks[next[index]] = new Track(targets[index], arrives);
            }
        }
    }

    private void Leave(bool animate)
    {
        for (var index = 0; index < keys.Length; index++)
        {
            if (!present.Contains(keys[index]) && tracks.Remove(keys[index], out var gone) && animate)
            {
                ghosts.Add(new Ghost(gone.Shown, Anchor(index)));
            }
        }
    }

    public void Snap()
    {
        foreach (var track in tracks.Values)
        {
            track.Snap();
        }

        ghosts.Clear();
        IsActive = false;
    }

    public void Forget()
    {
        tracks.Clear();
        ghosts.Clear();
        keys = [];
        parents = [];
        IsActive = false;
    }

    public void Step(TimeSpan now)
    {
        var active = false;

        foreach (var track in tracks.Values)
        {
            active |= track.Step(now);
        }

        for (var index = ghosts.Count - 1; index >= 0; index--)
        {
            if (ghosts[index].Step(now, this))
            {
                active = true;
            }
            else
            {
                ghosts.RemoveAt(index);
            }
        }

        IsActive = active;
    }

    private object Anchor(int removed)
    {
        for (var parent = removed < parents.Count ? parents[removed] : -1; parent >= 0; parent = parents[parent])
        {
            if (present.Contains(keys[parent]))
            {
                return keys[parent];
            }
        }

        return Nowhere;
    }

    private static double Progress(TimeSpan now, TimeSpan? start, TimeSpan delay, TimeSpan duration) =>
        start is { } began ? Math.Clamp((now - began - delay) / duration, 0, 1) : 0;

    internal sealed class Track(GraphRect target, bool arrives)
    {
        private GraphRect from = target;
        private TimeSpan? moved;
        private TimeSpan? arrived;
        private bool moving;
        private bool arriving = arrives;

        public GraphRect Target { get; private set; } = target;

        public GraphRect Shown { get; private set; } = target;

        public double Grown { get; private set; } = arrives ? 0 : 1;

        public double Opacity { get; private set; } = arrives ? 0 : 1;

        public double Lift { get; private set; } = arrives ? Rise : 0;

        public bool IsActive => moving || arriving;

        public bool IsFading => arriving || Opacity < 1;

        public void MoveTo(GraphRect target, bool animate)
        {
            if (target == Target)
            {
                return;
            }

            if (!animate)
            {
                Target = Shown = from = target;
                moving = false;

                return;
            }

            (from, Target, moved, moving) = (Shown, target, null, true);
        }

        public void Snap()
        {
            Shown = from = Target;
            (moving, arriving, Grown, Opacity, Lift) = (false, false, 1, 1, 0);
        }

        public bool Step(TimeSpan now)
        {
            if (moving)
            {
                moved ??= now;
                var share = Progress(now, moved, TimeSpan.Zero, Glide);
                Shown = Between(from, Target, Gentle.Ease(share));
                moving = share < 1;
            }

            if (arriving)
            {
                arrived ??= now;
                Grown = Drawing.Ease(Progress(now, arrived, TimeSpan.Zero, Grow));
                var shown = Progress(now, arrived, FadeDelay, Fade);
                Opacity = Gentle.Ease(shown);
                Lift = (1 - Opacity) * Rise;
                arriving = shown < 1;
            }

            return IsActive;
        }

        private static GraphRect Between(GraphRect from, GraphRect to, double share) =>
            new(from.X + ((to.X - from.X) * share), from.Y + ((to.Y - from.Y) * share), from.Width + ((to.Width - from.Width) * share), from.Height + ((to.Height - from.Height) * share));
    }

    internal sealed class Ghost(GraphRect rect, object anchor)
    {
        private readonly GraphRect origin = rect;
        private TimeSpan? started;

        public GraphRect Rect { get; private set; } = rect;

        public GraphPoint Toward { get; private set; }

        public bool IsAnchored { get; private set; }

        public double Remaining { get; private set; } = 1;

        public bool Step(TimeSpan now, AgentTreeMotion motion)
        {
            started ??= now;
            var share = Leaving.Ease(Progress(now, started, TimeSpan.Zero, Collapse));
            if (motion.tracks.TryGetValue(anchor, out var parent))
            {
                (Toward, IsAnchored) = (new GraphPoint(parent.Shown.Center.X, parent.Shown.Bottom), true);
            }

            var (width, height) = (origin.Width * (1 - (Shrink * share)), origin.Height * (1 - (Shrink * share)));
            var center = IsAnchored
                ? new GraphPoint(origin.Center.X + ((Toward.X - origin.Center.X) * Pull * share), origin.Center.Y + ((Toward.Y - origin.Center.Y) * Pull * share))
                : origin.Center;
            Rect = new GraphRect(center.X - (width / 2), center.Y - (height / 2), width, height);
            Remaining = 1 - share;

            return share < 1;
        }
    }
}
