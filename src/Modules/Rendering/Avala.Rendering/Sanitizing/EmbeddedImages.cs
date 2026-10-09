using System.Text.RegularExpressions;

namespace Avala.Rendering.Sanitizing;

internal static partial class EmbeddedImages
{
    public static bool Allow(string reference) => Embedded().IsMatch(reference.Trim());

    [GeneratedRegex(@"^data:image/(png|jpeg|gif|webp);base64,", RegexOptions.IgnoreCase)]
    private static partial Regex Embedded();
}
