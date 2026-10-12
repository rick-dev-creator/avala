using System.Collections.Immutable;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Processes;

namespace Avala.Preview.Running;

internal sealed class RunBook
{
    private ImmutableDictionary<ProcessTreeId, JobId> owners = ImmutableDictionary<ProcessTreeId, JobId>.Empty;

    public Option<JobId> OwnerOf(ProcessTreeId tree) =>
        Volatile.Read(ref owners).TryGetValue(tree, out var job) ? job : Option<JobId>.None;
}
