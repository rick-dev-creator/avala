using Avala.Server.ArchitectureTests.Layout;
using Avala.Server.ArchitectureTests.Rules;

namespace Avala.Server.ArchitectureTests.Tests;

public sealed class CqrsTests
{
    private static ServerScope Production => ServerScopes.Of(Scope.Production);

    private static ServerScope Violating => ServerScopes.Of(Scope.Violating);

    [Fact]
    public void MessagesLiveInContracts() =>
        Assert.Empty(CqrsRules.MessagesOutsideContracts(Production));

    [Fact]
    public void DetectsMessagesOutsideContracts() =>
        Assert.Equal(["Avala.Server.Violating.Application.RemindOrder"], Violations.Sorted(CqrsRules.MessagesOutsideContracts(Violating)));

    [Fact]
    public void MessagesAreCommandsOrQueries() =>
        Assert.Empty(CqrsRules.MessagesThatAreNotCommandsOrQueries(Production));

    [Fact]
    public void DetectsMessagesThatAreNotCommandsOrQueries() =>
        Assert.Equal(["Avala.Server.Violating.Contracts.PlaceOrder"], Violations.Sorted(CqrsRules.MessagesThatAreNotCommandsOrQueries(Violating)));

    [Fact]
    public void MessagesAnswerWithAResult() =>
        Assert.Empty(CqrsRules.MessagesAnsweringWithoutAResult(Production));

    [Fact]
    public void DetectsMessagesAnsweringWithoutAResult() =>
        Assert.Equal(["Avala.Server.Violating.Contracts.CountOrders"], Violations.Sorted(CqrsRules.MessagesAnsweringWithoutAResult(Violating)));

    [Fact]
    public void HandlersLiveInInfrastructure() =>
        Assert.Empty(CqrsRules.HandlersOutsideInfrastructure(Production));

    [Fact]
    public void DetectsHandlersOutsideInfrastructure() =>
        Assert.Equal(
            ["Avala.Server.Violating.Application.PlaceOrderHandler", "Avala.Server.Violating.Contracts.ShipOrderHandler"],
            Violations.Sorted(CqrsRules.HandlersOutsideInfrastructure(Violating)));

    [Fact]
    public void HandlersAreCommandOrQueryHandlers() =>
        Assert.Empty(CqrsRules.HandlersThatAreNotCommandOrQueryHandlers(Production));

    [Fact]
    public void DetectsHandlersOfPlainRequests() =>
        Assert.Equal(["Avala.Server.Violating.Application.PlaceOrderHandler"], Violations.Sorted(CqrsRules.HandlersThatAreNotCommandOrQueryHandlers(Violating)));

    [Fact]
    public void HandlersWorkOnTheDbContextDirectly() =>
        Assert.Empty(CqrsRules.HandlerCollaboratorsBesidesTheDbContext(Production));

    [Fact]
    public void DetectsHandlersBehindAnAbstraction() =>
        Assert.Equal(["CountOrdersHandler(IOrderStore)"], Violations.Sorted(CqrsRules.HandlerCollaboratorsBesidesTheDbContext(Violating)));

    [Fact]
    public void NoModuleHasRepositoriesOrUnitsOfWork() =>
        Assert.Empty(CqrsRules.RepositoryAbstractions(Production));

    [Fact]
    public void DetectsRepositories() =>
        Assert.Equal(
            ["Avala.Server.Violating.Application.IOrderRepository", "Avala.Server.Violating.Infrastructure.OrderRepository"],
            Violations.Sorted(CqrsRules.RepositoryAbstractions(Violating)));
}
