using Avala.Components.Pictures;
using Avala.Jobs.Contracts;
using Avala.Workbench.Linking;
using Avala.Workbench.Reviewing;

namespace Avala.Workbench.Conversation;

internal sealed class FileViewing(WorkedFiles files, Links links, IPictureDecoder pictures, CodeFiles code)
{
    public CodeFiles Code => code;

    public WorkedFiles Files => files;

    public Links Links => links;

    public IPictureDecoder Pictures => pictures;

    public FileViews For(JobId job) => new(job, this);
}

internal sealed class FileViews(JobId job, FileViewing viewing)
{
    public FileChangesViewModel Changes(string path) => new(job, path, viewing.Files);

    public FilePreviewViewModel Preview(string path) => new(job, path, viewing);
}
