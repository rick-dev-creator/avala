using System.Globalization;
using Avala.Notes.Contracts;
using Avala.Notes.Notebook;
using Avala.Notes.Taking;
using Avala.Sdk;

namespace Avala.Notes.NoteFiles;

internal sealed class NoteShelf : INoteShelf
{
    public const long MediaLimit = 20 * 1024 * 1024;

    public async Task<Result<NoteRecord, NoteRejection>> WriteAsync(NoteRecord note, CancellationToken cancellationToken)
    {
        var folder = note.Folder.Length > 0 ? note.Folder : Unused(note.Root, $"{NoteFolder.Of(note.Place)}/{NoteFolder.Named(note.Created, note.Title)}");
        var placed = note with { Folder = folder };

        try
        {
            Directory.CreateDirectory(Absolute(placed.Root, folder));
            await File.WriteAllTextAsync(Path.Combine(Absolute(placed.Root, folder), NoteFolder.Document), NoteDocument.Written(placed), cancellationToken);

            return placed;
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return NoteRejection.Unwritable;
        }
    }

    private static Result<NoteMedia, NoteRejection> Check(string file)
    {
        var info = new FileInfo(file);

        return !NoteMediaTypes.IsImage(file) ? NoteRejection.NotAnImage
            : !info.Exists ? NoteRejection.MissingMedia
            : info.Length > MediaLimit ? NoteRejection.MediaTooLarge
            : new NoteMedia(info.Name, NoteMediaTypes.Of(file));
    }

    public async Task<Result<NoteMedia, NoteRejection>> CopyAsync(NoteRecord note, string file, CancellationToken cancellationToken)
    {
        if (!Check(file).TryGetValue(out var media, out var refusal))
        {
            return refusal;
        }

        var folder = Absolute(note.Root, note.Folder);
        var name = UnusedFile(folder, media.File);

        try
        {
            await using var source = File.OpenRead(file);
            await using var target = File.Create(Path.Combine(folder, name));
            await source.CopyToAsync(target, cancellationToken);

            return media with { File = name };
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            return NoteRejection.Unwritable;
        }
    }

    public async Task<IReadOnlyList<NoteRecord>> ReadAsync(string root, NotePlace place, CancellationToken cancellationToken)
    {
        var shelf = Absolute(root, NoteFolder.Of(place));

        if (!Directory.Exists(shelf))
        {
            return [];
        }

        var notes = new List<NoteRecord>();

        foreach (var folder in Directory.EnumerateDirectories(shelf).Order(StringComparer.Ordinal))
        {
            var document = Path.Combine(folder, NoteFolder.Document);

            if (!File.Exists(document))
            {
                continue;
            }

            var media = Directory.EnumerateFiles(folder)
                .Where(NoteMediaTypes.IsImage)
                .Select(path => new NoteMedia(Path.GetFileName(path), NoteMediaTypes.Of(path)))
                .OrderBy(found => found.File, StringComparer.Ordinal)
                .ToList();
            var read = NoteDocument.Read(await File.ReadAllTextAsync(document, cancellationToken), place.Repository, media);
            notes.AddRange(read.Match<NoteRecord[]>(
                note => [note with { Root = root, Folder = $"{NoteFolder.Of(place)}/{Path.GetFileName(folder)}" }],
                () => []));
        }

        return notes;
    }

    private static string Absolute(string root, string folder) => Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));

    private static string Unused(string root, string folder)
    {
        var number = 1;

        while (Directory.Exists(Absolute(root, Numbered(folder, number))))
        {
            number++;
        }

        return Numbered(folder, number);
    }

    private static string UnusedFile(string folder, string name)
    {
        var number = 1;

        while (File.Exists(Path.Combine(folder, NumberedFile(name, number))))
        {
            number++;
        }

        return NumberedFile(name, number);
    }

    private static string Numbered(string folder, int number) =>
        number == 1 ? folder : string.Create(CultureInfo.InvariantCulture, $"{folder}-{number}");

    private static string NumberedFile(string name, int number) =>
        number == 1 ? name : string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileNameWithoutExtension(name)}-{number}{Path.GetExtension(name)}");
}
