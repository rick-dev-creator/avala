using System.Globalization;

namespace Avala.Workbench.Conversation;

internal static class MediaPhrases
{
    public static string Position(int index, int count) =>
        string.Create(CultureInfo.InvariantCulture, $"{index + 1} of {count}");
}
