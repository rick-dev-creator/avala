using System.Runtime.CompilerServices;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Microsoft.Extensions.Logging;

namespace Avala.Workbench.Following;

internal sealed partial class LiveFeed(Pulse pulse, IUiDispatcher ui, ILogger<LiveFeed> logger, TimeProvider clock) : IPresentation, IDisposable
{
    public LiveFeed(Pulse pulse, IUiDispatcher ui, ILogger<LiveFeed> logger)
        : this(pulse, ui, logger, TimeProvider.System)
    {
    }

    public TimeProvider Clock { get; } = clock;

    private CancellationTokenSource? active;

    public event EventHandler<Presented>? Presented;

    public long Revision { get; private set; }

    public Task Following { get; private set; } = Task.CompletedTask;

    public bool IsActive => active is not null;

    public Option<string> Failure { get; private set; } = Option<string>.None;

    public void Start<T>(Func<CancellationToken, ValueTask<T>> read, Action<T> show, [CallerFilePath] string component = "") =>
        Start(read, show, _ => { }, component);

    public void Start<T>(Func<CancellationToken, ValueTask<T>> read, Action<T> show, Action<Exception> fail, [CallerFilePath] string component = "")
    {
        if (active is null)
        {
            active = new CancellationTokenSource();
            var following = active.Token;
            component = Path.GetFileNameWithoutExtension(component);
            Failure = Option<string>.None;
            Following = Task.Run(() => FollowAsync(read, Shown(show), Reported(component, fail), following), CancellationToken.None);
        }
    }

    public void Refresh() => pulse.Beat();

    public void Stop()
    {
        active?.Cancel();
        active?.Dispose();
        active = null;
    }

    public void Dispose() => Stop();

    private Action<T> Shown<T>(Action<T> show) =>
        state =>
        {
            show(state);
            Failure = Option<string>.None;
        };

    private Action<Exception> Reported(string component, Action<Exception> fail) =>
        exception =>
        {
            Failure = exception.Message;
            LogFailed(component, exception);

            try
            {
                fail(exception);
            }
            catch (Exception failed)
            {
                LogFailed(component, failed);
            }
        };

    private async Task FollowAsync<T>(Func<CancellationToken, ValueTask<T>> read, Action<T> show, Action<Exception> fail, CancellationToken following)
    {
        try
        {
            await foreach (var _ in pulse.ChangesAsync(following))
            {
                var next = await ReadAsync(read, show, fail, following);
                await ui.InvokeAsync(() => ShowWhileFollowing(next, fail, following), following);
            }
        }
        catch (OperationCanceledException) when (following.IsCancellationRequested)
        {
        }
    }

    private static async Task<Action> ReadAsync<T>(Func<CancellationToken, ValueTask<T>> read, Action<T> show, Action<Exception> fail, CancellationToken following)
    {
        try
        {
            var state = await read(following);

            return () => show(state);
        }
        catch (Exception exception)
        {
            return following.IsCancellationRequested ? () => { } : () => fail(exception);
        }
    }

    private void ShowWhileFollowing(Action next, Action<Exception> fail, CancellationToken following)
    {
        if (following.IsCancellationRequested)
        {
            return;
        }

        try
        {
            next();
        }
        catch (Exception exception)
        {
            fail(exception);
        }

        Revision++;
        Presented?.Invoke(this, new Presented(Revision));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "{Component} failed to read or show what it follows, and keeps following")]
    private partial void LogFailed(string component, Exception exception);
}
