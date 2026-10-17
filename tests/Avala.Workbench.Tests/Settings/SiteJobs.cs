using Avala.Workbench.Navigation;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Settings;

internal static class SiteJobs
{
    public static IMessenger Messenger { get; } = new StrongReferenceMessenger();

    public static JobFocus Focus() => Focus(Messenger);

    public static JobFocus Focus(IMessenger messenger) => new(new TestRegions(), messenger);
}
