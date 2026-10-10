using Avala.Handoffs.Contracts;
using Avala.Handoffs.Policy;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Handoffs.Watching;

internal interface ILimitRules
{
    ValueTask<LimitRules> OfWorktreeAsync(string worktree, CancellationToken cancellationToken);
}

internal sealed record KeptWait(JobId Job, Option<string> Pending, DateTimeOffset Since);

internal interface IHandoffStore
{
    Task<IReadOnlyList<HandoffRecord>> HandoffsAsync(CancellationToken cancellationToken);

    Task AddAsync(HandoffRecord handoff, CancellationToken cancellationToken);

    Task<IReadOnlyList<KeptWait>> WaitsAsync(CancellationToken cancellationToken);

    Task KeepAsync(KeptWait wait, CancellationToken cancellationToken);

    Task DropAsync(JobId job, CancellationToken cancellationToken);
}
