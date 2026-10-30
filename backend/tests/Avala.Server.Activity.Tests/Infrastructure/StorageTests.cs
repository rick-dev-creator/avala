using Avala.Server.Activity.Infrastructure.Storage;
using Contract = Avala.Server.Activity.Contracts;
using Model = Avala.Server.Activity.Domain;

namespace Avala.Server.Activity.Tests.Infrastructure;

public sealed class StorageTests
{
    public static TheoryData<Type, Type> Enums => new()
    {
        { typeof(Contract.JobStatus), typeof(Model.JobStatus) },
        { typeof(Contract.HoldReason), typeof(Model.HoldReason) },
        { typeof(Contract.VerificationOutcome), typeof(Model.VerificationOutcome) },
        { typeof(Contract.CostBasis), typeof(Model.CostBasis) },
    };

    [Fact]
    public void AnInstancesLockKeyIsStableAcrossProcesses() =>
        Assert.Equal(1_922_692_640, ActivityLocks.Of(Guid.Parse("0199f6a0-0000-7000-8000-000000000003")));

    [Theory]
    [MemberData(nameof(Enums))]
    public void EveryContractEnumNamesTheDomainsValuesInOrder(Type contract, Type domain) =>
        Assert.Equal(Enum.GetNames(domain), Enum.GetNames(contract));

    [Fact]
    public void AListedJobsCostsAreOrderedByCurrencyThenBasis()
    {
        var at = new Model.EventTime(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero), Clamped: false);
        var job = Model.FleetJob.Start(Guid.NewGuid(), Guid.NewGuid(), at);
        job.Spend(new Model.TokenCounts(1, 0, 0, 0, 0), new Model.CostAmount(0.1m, "USD", Model.CostBasis.ApiEquivalent), at);
        job.Spend(new Model.TokenCounts(1, 0, 0, 0, 0), new Model.CostAmount(0.2m, "USD", Model.CostBasis.Billed), at);
        job.Spend(new Model.TokenCounts(1, 0, 0, 0, 0), new Model.CostAmount(0.3m, "EUR", Model.CostBasis.ApiEquivalent), at);

        var listed = job.ToContract("desktop");

        Assert.Equal(["EUR ApiEquivalent", "USD Billed", "USD ApiEquivalent"], listed.Costs.Select(cost => $"{cost.Currency} {cost.Basis}"));
    }
}
