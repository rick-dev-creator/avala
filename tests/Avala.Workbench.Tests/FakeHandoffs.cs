using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Tests;

internal sealed class FakeHandoffs : IHandoffs
{
    public Dictionary<JobId, IReadOnlyList<HandoffRecord>> Records { get; } = [];

    public Dictionary<JobId, ResetWait> Waits { get; } = [];

    public IReadOnlyList<HandoffRecord> OfJob(JobId job) => Records.GetValueOrDefault(job, []);

    public Option<ResetWait> WaitOf(JobId job) => Waits.TryGetValue(job, out var wait) ? wait : Option<ResetWait>.None;
}
