using System.Collections.ObjectModel;

namespace Avala.Workbench.Conversation;

internal static class TimelineOrder
{
    public static void Sync(ObservableCollection<ITimelineItem> shown, IReadOnlyList<ITimelineItem> target)
    {
        for (var position = 0; position < target.Count; position++)
        {
            var item = target[position];

            if (position < shown.Count && ReferenceEquals(shown[position], item))
            {
                continue;
            }

            var at = IndexOf(shown, item, position + 1);

            if (at >= 0)
            {
                shown.RemoveAt(at);
            }

            shown.Insert(position, item);
        }

        while (shown.Count > target.Count)
        {
            shown.RemoveAt(shown.Count - 1);
        }
    }

    private static int IndexOf(ObservableCollection<ITimelineItem> shown, ITimelineItem item, int from)
    {
        for (var index = from; index < shown.Count; index++)
        {
            if (ReferenceEquals(shown[index], item))
            {
                return index;
            }
        }

        return -1;
    }
}
