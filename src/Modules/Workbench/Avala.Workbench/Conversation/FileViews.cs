using Avala.Jobs.Contracts;
using Avala.Workbench.Reviewing;

namespace Avala.Workbench.Conversation;

internal sealed class FileViews(JobId job, WorkedFiles files)
{
    public FileChangesViewModel Changes(string path) => new(job, path, files);
}
