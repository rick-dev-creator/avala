using Avala.ArchitectureTests.Scopes;
using Avala.ArchitectureTests.Views;

namespace Avala.ArchitectureTests.Rules;

public sealed class ViewRuleTests
{
    public static TheoryData<string> EveryRule => [.. Enum.GetNames<ViewRule>()];

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(EveryRule))]
    public async Task ProductionRespectsTheRuleAsync(string rule)
    {
        var findings = await ViewFindings.OfAsync(Scope.Production, Enum.Parse<ViewRule>(rule), Cancellation);

        Assert.Empty(findings.Where(finding => !PendingRetrofit.Exempts(finding)).Select(finding => finding.Message));
    }

    [Theory]
    [MemberData(nameof(EveryRule))]
    public async Task TheCompliantFixtureRespectsTheRuleAsync(string rule) =>
        Assert.Empty((await ViewFindings.OfAsync(Scope.Compliant, Enum.Parse<ViewRule>(rule), Cancellation)).Select(finding => finding.Message));

    [Theory]
    [InlineData("ViewForEveryViewModel", "AvaloniaAwareViewModel,BasketViewModel,CouponViewModel,DomainAwareViewModel,InfrastructureAwareViewModel,ItemViewModel,SprawlingViewModel")]
    [InlineData("ViewModelForEveryView", "OversizedView,UntypedView")]
    [InlineData("CompiledBindings", "UntypedView.axaml")]
    [InlineData("DesignTimeDataContext", "UntypedView.axaml")]
    [InlineData("ViewModelInterfaces", "AvaloniaAwareViewModel,BasketViewModel,CouponViewModel,DomainAwareViewModel,InfrastructureAwareViewModel,ItemViewModel,SprawlingViewModel")]
    [InlineData("DesignTimeImplementations", "IVoucherViewModel")]
    [InlineData("NoUiFrameworkInViewModels", "AvaloniaAwareViewModel")]
    [InlineData("PresentationOnlyCodeBehind", "UntypedView.axaml.cs,UntypedView.axaml.cs")]
    [InlineData("ComponentSize", "OversizedView.axaml,OversizedView.axaml.cs,SprawlingViewModel.cs")]
    [InlineData("DeclaredRegions", "GhostRegistration")]
    [InlineData("NoParentOrSiblingReferences", "BasketViewModel,CouponViewModel,ItemViewModel")]
    [InlineData("ScriptedAcceptanceTests", "AvaloniaAwareViewModel,BasketViewModel,CouponViewModel,DomainAwareViewModel,InfrastructureAwareViewModel,ItemViewModel,OversizedView,SprawlingViewModel,UntypedView")]
    [InlineData("ThemeResourcesOnly", "UntypedView.axaml,UntypedView.axaml,UntypedView.axaml,UntypedView.axaml,UntypedView.axaml,UntypedView.axaml")]
    [InlineData("CommandsNotEventHandlers", "UntypedView.axaml")]
    [InlineData("NamedIconButtons", "UntypedView.axaml")]
    [InlineData("TypedRegionReferences", "UntypedView.axaml")]
    public async Task TheViolatingFixtureBreaksTheRuleWhereExpectedAsync(string rule, string subjects)
    {
        var findings = await ViewFindings.OfAsync(Scope.Violating, Enum.Parse<ViewRule>(rule), Cancellation);

        Assert.Equal(subjects.Split(','), findings.Select(finding => finding.Subject).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheViolatingFixtureCoversEveryRule()
    {
        var covered = typeof(ViewRuleTests).GetMethod(nameof(TheViolatingFixtureBreaksTheRuleWhereExpectedAsync))!
            .GetCustomAttributes(typeof(InlineDataAttribute), false)
            .Cast<InlineDataAttribute>()
            .Select(data => (string)data.Data[0]!);

        Assert.Equal(Enum.GetNames<ViewRule>().Order(StringComparer.Ordinal), covered.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task TheRetrofitScopeOnlyShrinksAsync()
    {
        Assert.Subset(PendingRetrofit.Ceiling.ToHashSet(), PendingRetrofit.Scoped.ToHashSet());

        foreach (var (rule, module) in PendingRetrofit.Scoped)
        {
            var findings = await ViewFindings.OfAsync(Scope.Production, rule, Cancellation);

            Assert.True(
                findings.Any(finding => finding.Module == module),
                $"{module} now respects {rule}: remove ({rule}, {module}) from PendingRetrofit.Scoped and from its Ceiling.");
        }
    }
}
