namespace Avala.Components.UI.Markdown;

public static class MarkdownBlocks
{
    public static int Settled(string text)
    {
        var settled = 0;
        var fence = string.Empty;
        var start = 0;

        while (start < text.Length)
        {
            var end = text.IndexOf('\n', start);
            var next = end < 0 ? text.Length : end + 1;

            if (end < 0)
            {
                break;
            }

            var line = text[start..end].TrimEnd('\r');
            fence = Fenced(line, fence);

            if (fence.Length == 0 && line.Trim().Length == 0 && start > 0)
            {
                settled = next;
            }

            start = next;
        }

        return settled;
    }

    private static string Fenced(string line, string open)
    {
        var trimmed = line.TrimStart(' ');

        if (line.Length - trimmed.Length > 3)
        {
            return open;
        }

        var marker = trimmed.StartsWith("```", StringComparison.Ordinal) ? "```"
            : trimmed.StartsWith("~~~", StringComparison.Ordinal) ? "~~~"
            : string.Empty;

        return marker.Length == 0 ? open
            : open.Length == 0 ? marker
            : marker == open && trimmed.Trim().Trim(marker[0]).Length == 0 ? string.Empty
            : open;
    }
}
