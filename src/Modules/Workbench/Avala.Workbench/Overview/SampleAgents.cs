using Avala.Jobs.Contracts;

namespace Avala.Workbench.Overview;

internal static class SampleAgents
{
    public static JobId ZodUpdate { get; } = new(Guid.Parse("0199c3a1-7a10-7000-8000-0000000000a1"));

    public static JobId CallSites { get; } = new(Guid.Parse("0199c3a1-7a10-7000-8000-0000000000a2"));

    public static JobId WebhookTests { get; } = new(Guid.Parse("0199c3a1-7a10-7000-8000-0000000000a3"));

    public static JobId ApiReference { get; } = new(Guid.Parse("0199c3a1-7a10-7000-8000-0000000000a4"));
}
