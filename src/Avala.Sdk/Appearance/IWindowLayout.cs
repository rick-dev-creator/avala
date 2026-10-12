namespace Avala.Sdk.Appearance;

public sealed record WindowLayout(bool IsSidebarCollapsed)
{
    public static WindowLayout Default { get; } = new(false);
}

public interface IWindowLayout
{
    ValueTask<WindowLayout> ReadAsync(CancellationToken cancellationToken);

    ValueTask RememberAsync(WindowLayout layout, CancellationToken cancellationToken);
}
