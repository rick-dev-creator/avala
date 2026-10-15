using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Tests;

internal sealed class FakeJobAttachments : IJobAttachments
{
    public List<string> Kept { get; } = [];

    public Option<AttachmentRefusal> Refusal { get; set; }

    public string MediaTypeOf(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".pdf" => "application/pdf",
        ".zip" => "application/zip",
        _ => "text/plain",
    };

    public ValueTask<Result<Attachment, AttachmentRefusal>> KeepAsync(string name, Stream content, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Stored(name));

    public ValueTask<Result<Attachment, AttachmentRefusal>> KeepFileAsync(string path, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Stored(Path.GetFileName(path)));

    private Result<Attachment, AttachmentRefusal> Stored(string name) =>
        Refusal.Match(
            Result<Attachment, AttachmentRefusal>.Failure,
            () =>
            {
                Kept.Add(name);

                return Result<Attachment, AttachmentRefusal>.Success(new Attachment(name, MediaTypeOf(name), $"/data/attachments/{Kept.Count}/{name}"));
            });
}

internal sealed class FakeFilePicker : IFilePicker
{
    public List<string> Picked { get; } = [];

    public ValueTask<IReadOnlyList<string>> PickFilesAsync(string title, CancellationToken cancellationToken) => ValueTask.FromResult<IReadOnlyList<string>>([.. Picked]);
}
