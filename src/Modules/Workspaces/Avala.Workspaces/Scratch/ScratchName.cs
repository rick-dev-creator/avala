using System.Globalization;

namespace Avala.Workspaces.Scratch;

internal static class ScratchName
{
    private const int Words = 4;

    private const int Longest = 40;

    private const string Unnamed = "job";

    public static string Of(DateOnly day, string instruction, string id) =>
        string.Create(CultureInfo.InvariantCulture, $"{day:yyyy-MM-dd}-{Slug(instruction)}-{id}");

    private static string Slug(string instruction)
    {
        var plain = new string([.. instruction.ToLowerInvariant().Select(letter => char.IsAsciiLetterOrDigit(letter) ? letter : ' ')]);
        var slug = string.Join('-', plain.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(Words));
        var cut = slug.Length > Longest ? slug[..Longest].TrimEnd('-') : slug;

        return cut.Length == 0 ? Unnamed : cut;
    }
}
