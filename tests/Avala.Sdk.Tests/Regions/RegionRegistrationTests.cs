using Avala.Sdk.Regions;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Sdk.Tests.Regions;

public sealed class RegionRegistrationTests
{
    [Fact]
    public void ARegisteredViewModelIsContributedToItsRegionWithItsOrder()
    {
        var registrar = new Registrar();
        registrar.Services.AddSingleton<UsageSection>();

        registrar.AddToRegion<UsageSection>(ShellRegions.Inspector, 30);

        using var services = registrar.Services.BuildServiceProvider();
        var contribution = Assert.Single(services.GetServices<RegionContribution>());
        Assert.Equal(ShellRegions.Inspector, contribution.Region);
        Assert.Equal(30, contribution.Order);
        Assert.Same(services.GetRequiredService<UsageSection>(), contribution.ViewModel);
    }

    [Fact]
    public void ContextOfAnotherTypeReachesARegionAwareViewModelAsNone()
    {
        var section = new UsageSection();
        IRegionAware aware = section;

        aware.ReceiveContext(Option<object>.Some(42));
        Assert.True(section.Job.IsNone);

        aware.ReceiveContext(Option<object>.Some("job-1"));
        Assert.True(section.Job.IsSome);
    }

    private sealed class Registrar : IPluginRegistrar
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
    }

    private sealed class UsageSection : IRegionAware<string>
    {
        public Option<string> Job { get; private set; }

        public void OnRegionContextChanged(Option<string> context) => Job = context;
    }
}
