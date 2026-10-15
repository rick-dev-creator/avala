using System.Collections.Frozen;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Jobs.Attaching;

internal sealed class AttachmentFolder(AvalaPaths paths) : IJobAttachments
{
    public const string Folder = "attachments";

    public const long MaximumBytes = 20 * 1024 * 1024;

    private static readonly FrozenDictionary<string, string> Binary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".pdf"] = "application/pdf",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> Textual = new[]
    {
        ".txt", ".md", ".markdown", ".log", ".json", ".jsonl", ".yaml", ".yml", ".toml", ".ini", ".xml", ".csv", ".tsv", ".html", ".css", ".scss",
        ".cs", ".csproj", ".sln", ".slnx", ".axaml", ".xaml", ".ts", ".tsx", ".js", ".jsx", ".mjs", ".py", ".rb", ".go", ".rs", ".java", ".kt", ".swift",
        ".c", ".h", ".cpp", ".hpp", ".sh", ".ps1", ".sql", ".graphql", ".proto", ".diff", ".patch", ".env", ".gitignore", ".dockerfile",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static string MediaTypeOf(string name)
    {
        var extension = Path.GetExtension(name);

        return Binary.TryGetValue(extension, out var known) ? known
            : Textual.Contains(extension) || extension.Length == 0 ? "text/plain"
            : "application/octet-stream";
    }

    public async ValueTask<Result<Attachment, AttachmentRefusal>> KeepAsync(string name, Stream content, CancellationToken cancellationToken)
    {
        var safe = Path.GetFileName(name) is { Length: > 0 } file ? file : "attachment";
        var folder = Path.Combine(paths.Data, Folder, Guid.NewGuid().ToString("N"));
        var target = Path.Combine(folder, safe);

        try
        {
            Directory.CreateDirectory(folder);
            await using var written = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
            var buffer = new byte[81920];
            long total = 0;
            int read;

            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                total += read;

                if (total > MaximumBytes)
                {
                    await written.DisposeAsync();
                    Directory.Delete(folder, recursive: true);

                    return AttachmentRefusal.TooLarge;
                }

                await written.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AttachmentRefusal.Unwritable;
        }

        return new Attachment(safe, MediaTypeOf(safe), target);
    }

    public async ValueTask<Result<Attachment, AttachmentRefusal>> KeepFileAsync(string path, CancellationToken cancellationToken)
    {
        FileStream source;

        try
        {
            source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AttachmentRefusal.Unreadable;
        }

        await using (source)
        {
            return source.Length > MaximumBytes ? AttachmentRefusal.TooLarge : await KeepAsync(Path.GetFileName(path), source, cancellationToken);
        }
    }
}
