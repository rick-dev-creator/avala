namespace Avala.Notes.Notebook;

internal static class NoteMediaTypes
{
    private static readonly Dictionary<string, string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
    };

    public static string Of(string file) => file.LastIndexOf('.') is >= 0 and var dot && Images.TryGetValue(file[dot..], out var type) ? type : string.Empty;

    public static bool IsImage(string file) => Of(file).Length > 0;
}
