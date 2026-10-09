using System.Text;

namespace Avala.Sdk;

public static class PortablePaths
{
    public const char Separator = '/';

    private const string PathEnds = "\"'`<>|*?()[]{},;";

    extension(string text)
    {
        public string Marking(string folder, string mark) => Rewritten(text, folder, mark, Separator);

        public string Unmarking(string mark, string folder, char separator) => Rewritten(text, mark, folder, separator);
    }

    private static string Rewritten(string text, string from, string to, char separator)
    {
        if (from.Length == 0)
        {
            return text;
        }

        var written = new StringBuilder(text.Length);
        var position = 0;

        for (var found = text.IndexOf(from, StringComparison.Ordinal); found >= 0; found = text.IndexOf(from, position, StringComparison.Ordinal))
        {
            written.Append(text, position, found - position).Append(to);
            position = found + from.Length;

            for (; position < text.Length && !char.IsWhiteSpace(text[position]) && !PathEnds.Contains(text[position], StringComparison.Ordinal); position++)
            {
                written.Append(text[position] is '/' or '\\' ? separator : text[position]);
            }
        }

        return written.Append(text, position, text.Length - position).ToString();
    }
}
