using Avala.Jobs.Contracts;
using Avala.Workbench.Linking;
using Avala.Workbench.Reviewing;

namespace Avala.Workbench.Conversation;

internal sealed class FileViews(JobId job, WorkedFiles files, Links links)
{
    public FileChangesViewModel Changes(string path) => new(job, path, files);

    public FilePreviewViewModel Preview(string path) => new(job, path, files, links);
}
