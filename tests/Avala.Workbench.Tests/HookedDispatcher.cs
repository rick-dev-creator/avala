using Avala.Sdk;

namespace Avala.Workbench.Tests;

internal sealed class HookedDispatcher(IUiDispatcher inner) : IUiDispatcher
{
    public Action? BeforeNext { get; set; }

    public bool Hooked { get; private set; }

    public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken)
    {
        if (BeforeNext is { } hook)
        {
            BeforeNext = null;
            Hooked = true;
            hook();
        }

        return inner.InvokeAsync(action, CancellationToken.None);
    }
}
