using Avala.Sdk.Appearance;

namespace Avala.Testing;

public sealed class RememberedLayout(WindowLayout remembered) : IWindowLayout
{
    public RememberedLayout()
        : this(WindowLayout.Default)
    {
    }

    public WindowLayout Remembered { get; private set; } = remembered;

    public ValueTask<WindowLayout> ReadAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Remembered);

    public ValueTask RememberAsync(Func<WindowLayout, WindowLayout> change, CancellationToken cancellationToken)
    {
        Remembered = change(Remembered);

        return ValueTask.CompletedTask;
    }
}
