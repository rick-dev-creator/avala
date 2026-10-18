using System.Collections.Concurrent;
using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Canvas.Tests.Showing;

internal sealed class KeepingAttachments(bool tooLarge = false) : IJobAttachments
{
    private readonly ConcurrentQueue<string> kept = new();

    public IReadOnlyList<string> Kept => [.. kept];

    public string MediaTypeOf(string name) => Path.GetExtension(name) switch
    {
        ".png" => "image/png",
        ".txt" => "text/plain",
        _ => "application/octet-stream",
    };

    public ValueTask<Result<Attachment, AttachmentRefusal>> KeepAsync(string name, Stream content, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public ValueTask<Result<Attachment, AttachmentRefusal>> KeepFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return ValueTask.FromResult<Result<Attachment, AttachmentRefusal>>(AttachmentRefusal.Unreadable);
        }

        if (tooLarge)
        {
            return ValueTask.FromResult<Result<Attachment, AttachmentRefusal>>(AttachmentRefusal.TooLarge);
        }

        kept.Enqueue(path);
        var name = Path.GetFileName(path);

        return ValueTask.FromResult<Result<Attachment, AttachmentRefusal>>(new Attachment(name, MediaTypeOf(name), Path.Combine("kept", name)));
    }
}
