using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Sidebar;

internal sealed record MoveTarget(string Name, IAsyncRelayCommand Move, bool IsNew = false)
{
    public static MoveTarget To(string name, Func<CancellationToken, Task> move) => new(name, new AsyncRelayCommand(move));

    public static MoveTarget ToNew(Func<CancellationToken, Task> move) => new(ProjectPhrases.NewProject, new AsyncRelayCommand(move), IsNew: true);
}
