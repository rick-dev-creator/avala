using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avala.Components.UI.Virtualizing;

public sealed class TimelinePanel : VirtualizingPanel
{
    private const double Fallback = 900;

    private static readonly object OwnContainer = new();
    private static readonly object Unkeyed = new();

    private readonly Dictionary<int, Control> realized = [];
    private readonly Dictionary<Control, object> keys = [];
    private readonly Dictionary<object, Stack<Control>> pool = [];
    private readonly Dictionary<object, Size> known = new(ReferenceEqualityComparer.Instance);
    private readonly HeightEstimates estimates = new();
    private double[] tops = [];
    private double[] heights = [];
    private double width;
    private Rect viewport;
    private bool hasViewport;
    private bool opening = true;
    private bool soughtEnd;
    private bool prefetching;
    private int target = -1;
    private (int First, int Last) shown = (0, -1);
    private (int First, int Last) kept = (0, -1);
    private (int Index, double Offset) anchor = (-1, 0);
    private (double Top, double Bottom) window;
    private IScrollAnchorProvider? anchors;

    public TimelinePanel() => EffectiveViewportChanged += OnEffectiveViewportChanged;

    protected override Size MeasureOverride(Size availableSize)
    {
        var items = Items;
        width = double.IsFinite(availableSize.Width) ? availableSize.Width : viewport.Width;
        Resize(items.Count);

        foreach (var index in realized.Keys.ToList())
        {
            Realize(index);
        }

        for (var index = 0; index < items.Count; index++)
        {
            heights[index] = Expected(items[index]);
        }

        shown = opening || !hasViewport || anchor.Index < 0 ? FromEnd(items.Count) : FromAnchor(items.Count);

        if (target >= 0 && target < items.Count)
        {
            Realize(target);
        }

        var total = Lay(items.Count);
        window = Window(total);
        Keep();
        Prefetch();

        return new Size(realized.Values.Select(container => container.DesiredSize.Width).DefaultIfEmpty(0).Max(), total);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var (index, container) in realized)
        {
            if (index < tops.Length)
            {
                container.Arrange(new Rect(0, tops[index], finalSize.Width, heights[index]));
            }
        }

        return finalSize;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        anchors = this.FindAncestorOfType<IScrollAnchorProvider>();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        anchors = null;
    }

    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(items, e);

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is { } added:
                Shift(e.NewStartingIndex, added.Count);
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is { } removed:
                for (var index = e.OldStartingIndex; index < e.OldStartingIndex + removed.Count; index++)
                {
                    Recycle(index);
                }

                Shift(e.OldStartingIndex + removed.Count, -removed.Count);
                break;
            default:
                foreach (var index in realized.Keys.ToList())
                {
                    Recycle(index);
                }

                opening = e.Action == NotifyCollectionChangedAction.Reset || opening;
                soughtEnd = false;
                break;
        }

        InvalidateMeasure();
    }

    protected override Control? ScrollIntoView(int index)
    {
        if (index < 0 || index >= Items.Count)
        {
            return null;
        }

        opening = opening && index == Items.Count - 1;
        target = index;
        InvalidateMeasure();
        UpdateLayout();
        var container = ContainerFromIndex(index);
        container?.BringIntoView();

        return container;
    }

    protected override Control? ContainerFromIndex(int index) => realized.GetValueOrDefault(index);

    protected override int IndexFromContainer(Control container) =>
        realized.FirstOrDefault(pair => ReferenceEquals(pair.Value, container), new(-1, container)).Key;

    protected override IEnumerable<Control>? GetRealizedContainers() => realized.Values;

    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        var count = Items.Count;
        var at = from is Control control ? IndexFromContainer(control) : -1;
        var to = direction switch
        {
            NavigationDirection.First => 0,
            NavigationDirection.Last => count - 1,
            NavigationDirection.Down or NavigationDirection.Next => at + 1,
            NavigationDirection.Up or NavigationDirection.Previous => at - 1,
            _ => -1,
        };

        to = wrap && count > 0 ? (to + count) % count : to;

        return to >= 0 && to < count ? ScrollIntoView(to) : null;
    }

    private void Resize(int count)
    {
        if (heights.Length != count)
        {
            heights = new double[count];
            tops = new double[count];
        }

        foreach (var index in realized.Keys.Where(index => index >= count).ToList())
        {
            Recycle(index);
        }
    }

    private double Expected(object? item)
    {
        if (item is null)
        {
            return 0;
        }

        if (!known.TryGetValue(item, out var size))
        {
            return estimates.Of(item, width);
        }

        return Math.Abs(size.Width - width) < 0.5 ? size.Height : Math.Max(0, estimates.Of(item, width) + size.Height - estimates.Of(item, size.Width));
    }

    private double Room() =>
        hasViewport && viewport.Height > 0 ? viewport.Height
        : TopLevel.GetTopLevel(this) is { Bounds.Height: > 0 } top ? top.Bounds.Height
        : Fallback;

    private (int First, int Last) FromEnd(int count)
    {
        var room = Room();
        var filled = 0d;
        var index = count - 1;

        while (index >= 0 && filled < room)
        {
            filled += Realize(index);
            index--;
        }

        return (index + 1, count - 1);
    }

    private (int First, int Last) FromAnchor(int count)
    {
        if (count == 0)
        {
            return (0, -1);
        }

        var first = Math.Min(anchor.Index, count - 1);
        var top = 0d;

        for (var index = 0; index < first; index++)
        {
            top += heights[index];
        }

        var bottom = top + anchor.Offset + viewport.Height;
        var last = first;

        while (last < count && top < bottom)
        {
            top += Realize(last);
            last++;
        }

        while (top < bottom && first > 0)
        {
            first--;
            top += Realize(first);
        }

        return (first, last - 1);
    }

    private (double Top, double Bottom) Window(double total)
    {
        var room = Room();

        if (opening || !hasViewport || anchor.Index < 0 || anchor.Index >= tops.Length)
        {
            return (Math.Max(0, total - room), total);
        }

        var top = Math.Max(0, Math.Min(tops[anchor.Index] + anchor.Offset, total - room));

        return (top, top + room);
    }

    private double Lay(int count)
    {
        var top = 0d;

        for (var index = 0; index < count; index++)
        {
            tops[index] = top;
            top += heights[index];
        }

        return top;
    }

    private void Keep()
    {
        var room = Room();
        var (above, below) = (window.Top - room, window.Bottom + room);
        var first = 0;

        while (first < tops.Length && tops[first] + heights[first] <= above)
        {
            first++;
        }

        var last = first;

        while (last < tops.Length && tops[last] < below)
        {
            last++;
        }

        kept = (Math.Min(first, shown.First), Math.Max(last - 1, shown.Last));

        foreach (var index in realized.Keys.Where(index => (index < kept.First || index > kept.Last) && index != target).ToList())
        {
            if (!realized[index].IsKeyboardFocusWithin)
            {
                Recycle(index);
            }
        }
    }

    private void Prefetch()
    {
        if (prefetching || Next() < 0)
        {
            return;
        }

        prefetching = true;
        Dispatcher.UIThread.Post(
            () =>
            {
                prefetching = false;

                if (Next() is var next and >= 0 && next < Items.Count && heights.Length == Items.Count)
                {
                    Realize(next);
                    InvalidateMeasure();
                }
            },
            DispatcherPriority.ApplicationIdle);
    }

    private int Next()
    {
        for (var index = shown.First - 1; index >= kept.First; index--)
        {
            if (!realized.ContainsKey(index))
            {
                return index;
            }
        }

        for (var index = shown.Last + 1; index <= kept.Last; index++)
        {
            if (!realized.ContainsKey(index))
            {
                return index;
            }
        }

        return -1;
    }

    private double Realize(int index)
    {
        if (!realized.TryGetValue(index, out var container))
        {
            container = Create(index);
            realized[index] = container;
        }

        container.Measure(new Size(width, double.PositiveInfinity));
        var height = container.DesiredSize.Height;
        heights[index] = height;

        if (Items[index] is { } item && known.GetValueOrDefault(item) != new Size(width, height))
        {
            known[item] = new Size(width, height);
            estimates.Learn(item, width, height);
        }

        return height;
    }

    private Control Create(int index)
    {
        var generator = ItemContainerGenerator ?? throw new InvalidOperationException("The timeline panel is not attached to a list.");
        var item = Items[index];
        Control container;

        if (generator.NeedsContainer(item, index, out var key))
        {
            var recycleKey = key ?? Unkeyed;

            if (pool.TryGetValue(recycleKey, out var stack) && stack.TryPop(out var recycled))
            {
                container = recycled;
                container.IsVisible = true;
                generator.PrepareItemContainer(container, item, index);
            }
            else
            {
                container = generator.CreateContainer(item, index, key);
                keys[container] = recycleKey;
                generator.PrepareItemContainer(container, item, index);
                AddInternalChild(container);
            }
        }
        else
        {
            container = (Control)item!;
            keys[container] = OwnContainer;
            generator.PrepareItemContainer(container, item, index);
            AddInternalChild(container);
        }

        generator.ItemContainerPrepared(container, item, index);
        anchors?.RegisterAnchorCandidate(container);

        return container;
    }

    private void Recycle(int index)
    {
        if (!realized.Remove(index, out var container))
        {
            return;
        }

        anchors?.UnregisterAnchorCandidate(container);

        if (keys.TryGetValue(container, out var key) && key != OwnContainer)
        {
            ItemContainerGenerator?.ClearItemContainer(container);
            container.IsVisible = false;

            if (!pool.TryGetValue(key, out var stack))
            {
                stack = new Stack<Control>();
                pool[key] = stack;
            }

            stack.Push(container);
        }
        else
        {
            keys.Remove(container);
            RemoveInternalChild(container);
        }
    }

    private void Shift(int from, int by)
    {
        var moved = realized.Where(pair => pair.Key >= from).ToList();

        foreach (var (index, _) in moved)
        {
            realized.Remove(index);
        }

        foreach (var (index, container) in moved)
        {
            realized[index + by] = container;
            ItemContainerGenerator?.ItemContainerIndexChanged(container, index, index + by);
        }

        target = target >= from ? target + by : target;
        anchor = anchor.Index >= from ? (Math.Max(0, anchor.Index + by), anchor.Offset) : anchor;
    }

    private void OnEffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
    {
        var seen = e.EffectiveViewport;
        var moved = !hasViewport || Moved(viewport, seen);
        hasViewport = seen.Height > 0 && seen.Width > 0;
        viewport = seen;

        if (!hasViewport || SeeksTheEnd(seen))
        {
            return;
        }

        opening = false;
        anchor = Anchor(seen.Top);
        target = target >= shown.First && target <= shown.Last ? -1 : target;

        if (moved)
        {
            InvalidateMeasure();
        }
    }

    private static bool Moved(Rect before, Rect after) =>
        Math.Abs(after.Top - before.Top) > 0.5 || Math.Abs(after.Height - before.Height) > 0.5 || Math.Abs(after.Width - before.Width) > 0.5;

    private bool SeeksTheEnd(Rect seen)
    {
        if (!opening || seen.Bottom >= Bounds.Height - 1 || soughtEnd)
        {
            return false;
        }

        soughtEnd = true;
        this.BringIntoView(new Rect(0, Math.Max(0, Bounds.Height - 1), 1, 1));

        return true;
    }

    private (int Index, double Offset) Anchor(double top)
    {
        if (tops.Length == 0 || tops.Length != heights.Length)
        {
            return (-1, 0);
        }

        var index = 0;

        while (index < tops.Length - 1 && tops[index] + heights[index] <= top)
        {
            index++;
        }

        return (index, top - tops[index]);
    }
}
