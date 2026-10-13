using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Sidebar;

internal sealed record MoveTarget(string Name, IAsyncRelayCommand Move)
{
    public static MoveTarget To(string name, Func<CancellationToken, Task> move) => new(name, new AsyncRelayCommand(move));
}
