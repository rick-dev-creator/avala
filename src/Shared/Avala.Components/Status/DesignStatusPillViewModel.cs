namespace Avala.Components.Status;

public sealed class DesignStatusPillViewModel : IStatusPillViewModel
{
    public StatusKind Kind => StatusKind.Held;

    public string Text => "Held · stalled";

    public IStatusDotViewModel Dot { get; } = new StatusDotViewModel(StatusKind.Held);
}
