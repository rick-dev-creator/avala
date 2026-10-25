using System.Buffers;
using System.Text;

namespace Avala.Server.Activity.Domain;

internal static class StoredText
{
    public static bool IsStorable(string text)
    {
        var remaining = text.AsSpan();

        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done || Rune.IsControl(rune))
            {
                return false;
            }

            remaining = remaining[consumed..];
        }

        return true;
    }
}
