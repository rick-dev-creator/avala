using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Sdk.Regions;
using Avala.Workbench.Contracts.Presentation;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Navigation;

internal sealed class JobFocus(IRegions regions, IMessenger messenger)
{
    public void Select(JobId job) => messenger.Send(new JobSelected(job));

    public void Follow(IRecipient<JobSelected> recipient) => messenger.Register(recipient);

    public void Show(IPage page) => messenger.Send(new PageRequested(page));

    public void Inspect(Option<JobId> job) =>
        _ = job.Match(
            found =>
            {
                regions.SetContext(ShellRegions.Inspector, found);
                return true;
            },
            () =>
            {
                regions.ClearContext(ShellRegions.Inspector);
                return false;
            });
}
