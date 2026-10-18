using System.Globalization;
using System.Text;
using Avala.Notes.Contracts;

namespace Avala.Notes.Notebook;

internal static class NoteFolder
{
    public const string Notes = ".avala/notes";

    public const string Document = "note.md";

    private const int SlugLength = 48;

    public static string Of(NotePlace place) => place.Job.Match(job => $"{Notes}/{job.Value:N}", () => $"{Notes}/project");

    public static string Named(DateTimeOffset created, string title)
    {
        var slug = Slug(title);
        var stamp = created.UtcDateTime.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture);

        return slug.Length == 0 ? stamp : $"{stamp}-{slug}";
    }

    private static string Slug(string title)
    {
        var slug = new StringBuilder();

        foreach (var letter in title.Normalize(NormalizationForm.FormD))
        {
            if (char.IsAsciiLetterOrDigit(letter))
            {
                slug.Append(char.ToLowerInvariant(letter));
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(letter) != UnicodeCategory.NonSpacingMark && slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }

        var trimmed = slug.ToString().Trim('-');

        return trimmed.Length <= SlugLength ? trimmed : trimmed[..SlugLength].TrimEnd('-');
    }
}
