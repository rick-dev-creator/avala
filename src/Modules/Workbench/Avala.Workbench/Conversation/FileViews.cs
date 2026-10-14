using Avala.Components.Pictures;
using Avala.Jobs.Contracts;
using Avala.Workbench.Linking;
using Avala.Workbench.Reviewing;

namespace Avala.Workbench.Conversation;

internal sealed record ReplyTools(CodeFiles Code, JobReferences References);

internal sealed class FileViewing(WorkedFiles files, Links links, IPictureDecoder pictures, ReplyTools replies)
{
    public CodeFiles Code => replies.Code;

    public JobReferences References => replies.References;

    public WorkedFiles Files => files;

    public Links Links => links;

    public IPictureDecoder Pictures => pictures;

    public FileViews For(JobId job) => new(job, this);
}

internal sealed class FileViews(JobId job, FileViewing viewing)
{
    public FileChangesViewModel Changes(string path) => new(job, path, viewing.Files);

    public FilePreviewViewModel Preview(string path) => new(job, path, viewing);

    public CodeFiles Code => viewing.Code;

    public Links Links => viewing.Links;

    public CodePeekViewModel Peek() => new(job, viewing.Files);

    public Task<Avala.Sdk.Result<Uri, ReferenceRefusal>> OpenAsync(Reference reference, CancellationToken cancellationToken) => viewing.References.OpenAsync(job, reference, cancellationToken);
}
