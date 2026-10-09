using System.Collections.ObjectModel;
using Avala.Workbench.Reviewing;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Review;

internal interface IChangedFileViewModel
{
    string Path { get; }

    ChangeKind Kind { get; }

    string Counts { get; }

    IReadOnlyList<IHunkViewModel> Hunks { get; }

    bool IsExpanded { get; }

    string Error { get; }

    IAsyncRelayCommand ShowHunksCommand { get; }
}

internal interface IHunkViewModel
{
    string Header { get; }

    IReadOnlyList<HunkLine> Lines { get; }
}

internal sealed record HunkLine(string Text, DiffLineKind Kind)
{
    public bool IsAdded => Kind == DiffLineKind.Added;

    public bool IsRemoved => Kind == DiffLineKind.Removed;
}

[INotifyPropertyChanged]
internal sealed partial class ChangedFileViewModel : IChangedFileViewModel
{
    private readonly WorkspaceId workspace;
    private readonly ReviewReader reader;
    private readonly ObservableCollection<HunkViewModel> hunks = [];

    public ChangedFileViewModel(FileChange file, WorkspaceId workspace, ReviewReader reader)
    {
        this.workspace = workspace;
        this.reader = reader;
        Path = file.Path;
        Kind = file.Kind;
        Counts = ReviewPhrases.Counts(file);
        Error = string.Empty;
    }

    public string Path { get; }

    public ChangeKind Kind { get; }

    public string Counts { get; }

    public IReadOnlyList<IHunkViewModel> Hunks => hunks;

    [ObservableProperty]
    public partial bool IsExpanded { get; private set; }

    [ObservableProperty]
    public partial string Error { get; private set; }

    [RelayCommand]
    private async Task ShowHunksAsync(CancellationToken cancellationToken)
    {
        if (IsExpanded)
        {
            hunks.Clear();
            IsExpanded = false;

            return;
        }

        var diff = await reader.HunksAsync(workspace, Path, cancellationToken);
        Error = diff.Match(
            found =>
            {
                foreach (var hunk in found.Hunks)
                {
                    hunks.Add(new HunkViewModel(hunk));
                }

                IsExpanded = true;

                return found.Binary ? "A binary file" : string.Empty;
            },
            _ => "The changes of this file could not be read.");
    }
}

internal sealed class HunkViewModel(DiffHunk hunk) : IHunkViewModel
{
    public string Header { get; } = $"@@ -{hunk.OldStart},{hunk.OldLines} +{hunk.NewStart},{hunk.NewLines} @@ {hunk.Section}".TrimEnd();

    public IReadOnlyList<HunkLine> Lines { get; } = [.. hunk.Lines.Select(line => new HunkLine(
        line.Kind switch
        {
            DiffLineKind.Added => $"+{line.Text}",
            DiffLineKind.Removed => $"-{line.Text}",
            _ => $" {line.Text}",
        },
        line.Kind))];
}
