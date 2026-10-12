using Avala.Preview.Contracts;
using Avala.Sdk;
using Avala.Sdk.Events;

namespace Avala.Preview.Navigation;

internal sealed class PreviewFeed(IEventFeed events, IUiDispatcher ui) : IDisposable
{
    private CancellationTokenSource? active;

    public Task Following { get; private set; } = Task.CompletedTask;

    public void Start(Action show)
    {
        if (active is not null)
        {
            return;
        }

        active = new CancellationTokenSource();
        var following = active.Token;
        Following = Task.WhenAll(
            FollowAsync<PreviewAvailable>(show, following),
            FollowAsync<PreviewGone>(show, following),
            FollowAsync<PreviewRunChanged>(show, following));
    }

    public void Stop()
    {
        active?.Cancel();
        active?.Dispose();
        active = null;
    }

    public void Dispose() => Stop();

    private async Task FollowAsync<TEvent>(Action show, CancellationToken following)
        where TEvent : IIntegrationEvent
    {
        try
        {
            await foreach (var _ in events.SubscribeAsync<TEvent>(following))
            {
                await ui.InvokeAsync(
                    () =>
                    {
                        if (!following.IsCancellationRequested)
                        {
                            show();
                        }
                    },
                    following);
            }
        }
        catch (OperationCanceledException) when (following.IsCancellationRequested)
        {
        }
    }
}
