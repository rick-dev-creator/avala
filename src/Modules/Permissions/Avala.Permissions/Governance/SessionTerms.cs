using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;

namespace Avala.Permissions.Governance;

internal sealed class SessionTerms(IPolicyFiles files, IEnumerable<IJobTerms> plugins)
{
    public IPolicyFiles Files => files;

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
