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
}
