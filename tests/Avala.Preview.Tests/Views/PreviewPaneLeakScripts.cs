using Avala.Jobs.Contracts;
using Avala.Preview.Pane;
using Avala.Preview.Tests.Pane;
using Avala.Testing.UI;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Preview.Tests.Views;

[Collection(Leaks.Collection)]
public sealed class PreviewPaneLeakScripts(HeadlessUi ui) : IDisposable
{
    private readonly PaneBench bench = new();

    [Fact]
    public Task PreviewingThirtyJobsOnADeviceAndLeavingEachReturnsMemoryToItsBaselineAsync() =>
        ui.RunAsync(async () =>
        {
            var pane = bench.Pane();
            var view = Screen.Show(pane);
            var port = 24_000;

            var retention = await Leaks.ManagedAcrossAsync(() =>
            {
                var job = JobId.New();
                bench.Serve(job, port++);
                pane.OnRegionContextChanged(job);
                bench.Messenger.Send(new PreviewRequested(job));
                pane.Frame.Choices.Single(choice => choice.Name == "iPhone").Choose.Execute(null);
                view.Settle();
                pane.Frame.Choices.Single(choice => choice.Name == "Responsive").Choose.Execute(null);
                pane.OnRegionContextChanged(bench.Other);
                view.Settle();

                return Task.CompletedTask;
            });

            Assert.All(bench.Surfaces.Opened, surface => Assert.True(surface.IsClosed));
            Assert.True(retention.IsBounded, retention.ToString());
            pane.Dispose();
        }, TestContext.Current.CancellationToken);

    public void Dispose() => bench.Dispose();
}
