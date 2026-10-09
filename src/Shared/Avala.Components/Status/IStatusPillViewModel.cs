namespace Avala.Components.Status;

public interface IStatusPillViewModel
{
    StatusKind Kind { get; }

    string Text { get; }

    IStatusDotViewModel Dot { get; }
}
