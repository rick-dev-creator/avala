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

    public ValueTask RememberAsync(WindowLayout layout, CancellationToken cancellationToken)
    {
        Remembered = layout;

        return ValueTask.CompletedTask;
    }
}
