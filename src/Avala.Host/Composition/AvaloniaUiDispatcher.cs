using Avala.Sdk;
using Avalonia.Threading;

namespace Avala.Host.Composition;

internal sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken) =>
        new(Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Normal, cancellationToken).GetTask());
}
