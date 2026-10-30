using Avala.Components.Privacy;
using Avala.Sdk.Regions;
using Avala.Workbench.Accounts;
using Avala.Workbench.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workbench.UI;

internal static class SettingsSectionsRegistration
{
    public static MachineSections Sections(IServiceProvider provider) =>
        new(
            provider.GetRequiredService<IDefaultConnectionViewModel>(),
            provider.GetRequiredService<INotificationsViewModel>(),
            provider.GetRequiredService<ISourceControlViewModel>(),
            provider.GetRequiredService<IPersonalDetailsViewModel>())
        {
            Harnesses = Contributed(provider, WorkbenchRegions.Settings),
            Fleet = Contributed(provider, WorkbenchRegions.Fleet),
        };

    private static IReadOnlyList<object> Contributed(IServiceProvider provider, RegionName region) =>
        [.. provider.GetServices<RegionContribution>().Where(contribution => contribution.Region == region).OrderBy(contribution => contribution.Order).Select(contribution => contribution.ViewModel)];
}
