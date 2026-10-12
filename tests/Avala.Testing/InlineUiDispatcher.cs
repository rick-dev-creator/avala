using Avala.Sdk;

namespace Avala.Testing;

public sealed class InlineUiDispatcher : IUiDispatcher
{
    public static InlineUiDispatcher Instance { get; } = new();

    public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken)
    {
        action();

        return ValueTask.CompletedTask;
    }
}
