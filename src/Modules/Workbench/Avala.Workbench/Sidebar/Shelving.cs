using Avala.Jobs.Contracts;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Sidebar;

internal static class Shelving
{
    public static IAsyncRelayCommand Idle { get; } = new AsyncRelayCommand(() => Task.CompletedTask, () => false);

    public static bool Archivable(JobStatus status) =>
        status is JobStatus.NeedsHelp or JobStatus.AwaitingReview or JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed;
}
