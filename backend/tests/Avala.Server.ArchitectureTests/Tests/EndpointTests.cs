using Avala.Server.ArchitectureTests.Layout;
using Avala.Server.ArchitectureTests.Rules;

namespace Avala.Server.ArchitectureTests.Tests;

public sealed class EndpointTests
{
    private static ServerScope Production => ServerScopes.Of(Scope.Production);

    private static ServerScope Violating => ServerScopes.Of(Scope.Violating);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void EndpointsLiveInEndPoints() =>
        Assert.Empty(EndpointRules.EndpointsOutsideEndPoints(Production));

    [Fact]
    public void DetectsEndpointsOutsideEndPoints() =>
        Assert.Equal(["Avala.Server.Violating.Module.StrayEndpoint"], Violations.Sorted(EndpointRules.EndpointsOutsideEndPoints(Violating)));

    [Fact]
    public void EndpointsAreNamedAfterTheirFeature() =>
        Assert.Empty(EndpointRules.EndpointsNotNamedAsEndpoints(Production));

    [Fact]
    public void DetectsEndpointsNotNamedAsEndpoints() =>
        Assert.Equal(["Avala.Server.Violating.EndPoints.Orders.OrderListing"], Violations.Sorted(EndpointRules.EndpointsNotNamedAsEndpoints(Violating)));

    [Fact]
    public void EndpointsExchangeOnlyTheirOwnRequestAndResponse() =>
        Assert.Empty(EndpointRules.EndpointsExchangingForeignTypes(Production));

    [Fact]
    public void DetectsEndpointsExposingContractsOrTheDomain() =>
        Assert.Equal(
            ["PlaceOrderEndpoint -> PlaceOrder", "ShowOrderEndpoint -> Order"],
            Violations.Sorted(EndpointRules.EndpointsExchangingForeignTypes(Violating)));

    [Fact]
    public void RequestsAndResponsesCarryOnlyTheirOwnTypes() =>
        Assert.Empty(EndpointRules.DtosCarryingForeignTypes(Production));

    [Fact]
    public void DetectsResponsesCarryingContracts() =>
        Assert.Equal(["PlaceOrderResponse.Order -> ArchivedOrder"], Violations.Sorted(EndpointRules.DtosCarryingForeignTypes(Violating)));

    [Fact]
    public void EveryEndpointHasAMapper() =>
        Assert.Empty(EndpointRules.EndpointsWithoutAMapper(Production));

    [Fact]
    public void DetectsEndpointsWithoutAMapper() =>
        Assert.Equal(
            ["Avala.Server.Violating.EndPoints.Orders.OrderListing", "Avala.Server.Violating.EndPoints.Placing.PlaceOrderEndpoint", "Avala.Server.Violating.Module.StrayEndpoint"],
            Violations.Sorted(EndpointRules.EndpointsWithoutAMapper(Violating)));

    [Fact]
    public void EachFeatureHoldsOneEndpoint() =>
        Assert.Empty(EndpointRules.FeaturesWithSeveralEndpoints(Production));

    [Fact]
    public void DetectsFeaturesWithSeveralEndpoints() =>
        Assert.Equal(["Avala.Server.Violating.EndPoints.Orders"], Violations.Sorted(EndpointRules.FeaturesWithSeveralEndpoints(Violating)));

    [Fact]
    public void OnlyMappersBuildCommandsAndQueries() =>
        Assert.Empty(EndpointRules.ContractsBuiltOutsideMappers(Production));

    [Fact]
    public void DetectsEndpointsBuildingCommandsThemselves() =>
        Assert.Equal(["Avala.Server.Violating.EndPoints.Placing.PlaceOrderEndpoint"], Violations.Sorted(EndpointRules.ContractsBuiltOutsideMappers(Violating)));

    [Fact]
    public async Task EveryEndpointNamesWhoMayCallItAsync() =>
        Assert.Empty(await EndpointRules.EndpointsNamingNoCallerAsync(Production, Cancellation));

    [Fact]
    public async Task DetectsEndpointsNamingNoCallerAsync() =>
        Assert.Equal(["OrderListing", "PlaceOrderEndpoint", "ShowOrderEndpoint"], await EndpointRules.EndpointsNamingNoCallerAsync(Violating, Cancellation));

    [Fact]
    public async Task NoEndpointIsAnonymousAsync() =>
        Assert.Empty(await EndpointRules.AnonymousEndpointsAsync(Production, Cancellation));

    [Fact]
    public async Task DetectsAnonymousEndpointsAsync() =>
        Assert.Equal(["PlaceOrderEndpoint.cs"], await EndpointRules.AnonymousEndpointsAsync(Violating, Cancellation));
}
