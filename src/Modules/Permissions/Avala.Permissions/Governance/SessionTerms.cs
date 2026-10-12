using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Permissions.Governance;

internal sealed class SessionTerms(IPolicyFiles files, IEnumerable<IJobTerms> plugins, Option<AvalaPaths> paths)
{
    public SessionTerms(IPolicyFiles files, IEnumerable<IJobTerms> plugins)
        : this(files, plugins, Option<AvalaPaths>.None)
    {
    }

    public SessionTerms(IPolicyFiles files, IEnumerable<IJobTerms> plugins, AvalaPaths paths)
        : this(files, plugins, Option<AvalaPaths>.Some(paths))
    {
    }

    public IPolicyFiles Files => files;

    public Option<string> ScratchOf(string workingDirectory) => paths.Map(known => ScratchFolders.Of(known, workingDirectory));

    public async Task<JobTerms> OfAsync(JobId job, CancellationToken cancellationToken)
    {
        var terms = JobTerms.Full;

        foreach (var plugin in plugins)
        {
            var declared = await plugin.OfAsync(job, cancellationToken);
            terms = new JobTerms(terms.ReadOnly || declared.ReadOnly, terms.AsksParent.IsSome ? terms.AsksParent : declared.AsksParent);
        }

        return terms;
    }
}
