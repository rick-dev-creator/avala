using Avala.Jobs.Contracts;

namespace Avala.Workbench.Presenting;

internal static class SampleJobs
{
    public static JobId JpyRounding { get; } = new(Guid.Parse("0199c3a1-7a10-7000-8000-000000000001"));

    public static JobId InvoicePdf { get; } = new(Guid.Parse("0199c3a1-7a10-7000-8000-000000000002"));

    public static JobId FlakyCheckout { get; } = new(Guid.Parse("0199c3a1-7a10-7000-8000-000000000003"));

    public static JobId SyncQueue { get; } = new(Guid.Parse("0199c3a1-7a10-7000-8000-000000000004"));

    public static JobId LoginRateLimit { get; } = new(Guid.Parse("0199c3a1-7a10-7000-8000-000000000005"));

    public static JobId LodashUpdate { get; } = new(Guid.Parse("0199c3a1-7a10-7000-8000-000000000006"));

    public static JobId CheckoutSplit { get; } = new(Guid.Parse("0199c3a1-7a10-7000-8000-000000000007"));
}
