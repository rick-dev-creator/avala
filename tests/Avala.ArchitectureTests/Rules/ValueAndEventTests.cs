using Avala.ArchitectureTests.Ddd;
using Avala.ArchitectureTests.Scopes;

namespace Avala.ArchitectureTests.Rules;

public sealed class ValueAndEventTests
{
    private static CodeScope Violating => CodeScopes.Of(Scope.Violating);

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void ValueObjectsAreReadOnly(Scope scope) =>
        Assert.Empty(ValueAndEventRules.MutableValueObjects(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsMutableValueObjects() =>
        Assert.Equal(["MutableAmount"], Violations.Named(ValueAndEventRules.MutableValueObjects(Violating)));

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void EventsAreImmutable(Scope scope) =>
        Assert.Empty(ValueAndEventRules.MutableEvents(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsMutableEvents() =>
        Assert.Equal(["MutableLedgerEvent"], Violations.Named(ValueAndEventRules.MutableEvents(Violating)));

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void DomainEventsLiveInTheDomain(Scope scope) =>
        Assert.Empty(ValueAndEventRules.MisplacedDomainEvents(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsMisplacedDomainEvents() =>
        Assert.Equal(["MisplacedDomainEvent"], Violations.Named(ValueAndEventRules.MisplacedDomainEvents(Violating)));

    [Theory]
    [InlineData(Scope.Production)]
    [InlineData(Scope.Compliant)]
    public void IntegrationEventsLiveInContracts(Scope scope) =>
        Assert.Empty(ValueAndEventRules.MisplacedIntegrationEvents(CodeScopes.Of(scope)));

    [Fact]
    public void DetectsMisplacedIntegrationEvents() =>
        Assert.Equal(["MisplacedIntegrationEvent"], Violations.Named(ValueAndEventRules.MisplacedIntegrationEvents(Violating)));
}
