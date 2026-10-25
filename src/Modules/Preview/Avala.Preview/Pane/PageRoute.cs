using Avala.Jobs.Contracts;
using Avala.Preview.Contracts;
using Avala.Preview.Navigation;
using Avala.Sdk;

namespace Avala.Preview.Pane;

internal sealed class PageRoute(PreviewDesk desk, DeviceFrameViewModel frame)
{
    private Option<Uri> inner;
    private bool wrapped;
    private int wraps;

    public Option<Uri> Seen(Option<IWebSurface> surface) => wrapped ? inner : surface.Bind(shown => shown.Address);

    public void Forget()
    {
        inner = Option<Uri>.None;
        wrapped = false;
    }

    public Task AgainAsync(Option<JobId> job, Option<IWebSurface> surface, Func<Option<IWebSurface>> current) =>
        surface.Bind(shown => job.Bind(owner => Seen(surface).Map(address => (Surface: shown, Owner: owner, Address: address))))
            .Match(found => GoAsync(found.Owner, found.Surface, found.Address, current), () => Task.CompletedTask);

    public Uri Outside(PreviewTarget target, Option<IWebSurface> surface) =>
        Seen(surface).Match(
            page => page.IsAbsoluteUri && page.Port != target.Address.Port && page.Scheme is "http" or "https" ? new Uri(target.Address, page.PathAndQuery) : page,
            () => target.Address);

    public Task GoAsync(JobId owner, IWebSurface surface, Uri address, Func<Option<IWebSurface>> current)
    {
        inner = address;
        wrapped = false;
        var asked = ++wraps;
        var around = PreviewDesk.IsViewer(address) ? Option<DeviceView>.None : frame.Depth;

        if (around.IsNone)
        {
            surface.Navigate(address);

            return Task.CompletedTask;
        }

        return around.Match(device => WrapAsync(owner, surface, device, address, asked, current), () => Task.CompletedTask);
    }

    private async Task WrapAsync(JobId owner, IWebSurface surface, DeviceView device, Uri address, int asked, Func<Option<IWebSurface>> current)
    {
        var around = await desk.DeviceAsync(owner, device, address, asked, CancellationToken.None);

        if (current() != Option<IWebSurface>.Some(surface) || wraps != asked)
        {
            return;
        }

        wrapped = around.Match(_ => true, _ => false);
        surface.Navigate(around.Match(viewer => viewer, _ => address));
    }
}
