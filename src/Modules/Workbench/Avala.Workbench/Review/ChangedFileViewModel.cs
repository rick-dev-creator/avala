using System.Collections.ObjectModel;
using Avala.Workbench.Reviewing;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Review;

[INotifyPropertyChanged]
internal sealed partial class ChangedFileViewModel
{
    private readonly WorkspaceId workspace;
    private readonly ReviewReader reader;

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

    public ObservableCollection<HunkViewModel> Hunks { get; } = [];

    [ObservableProperty]
    public partial bool IsExpanded { get; private set; }

    [ObservableProperty]
    public partial string Error { get; private set; }

    [RelayCommand]
    private async Task ShowHunksAsync(CancellationToken cancellationToken)
    {
        if (IsExpanded)
        {
            Hunks.Clear();
            IsExpanded = false;

            return;
        }

        var diff = await reader.HunksAsync(workspace, Path, cancellationToken);
        Error = diff.Match(
            found =>
            {
                foreach (var hunk in found.Hunks)
                {
                    Hunks.Add(new HunkViewModel(hunk));
                }

                IsExpanded = true;

                return found.Binary ? "A binary file" : string.Empty;
            },
            _ => "The changes of this file could not be read.");
    }
}

internal sealed class HunkViewModel(DiffHunk hunk)
{
    public string Header { get; } = $"@@ -{hunk.OldStart},{hunk.OldLines} +{hunk.NewStart},{hunk.NewLines} @@ {hunk.Section}".TrimEnd();

    public IReadOnlyList<string> Lines { get; } = [.. hunk.Lines.Select(line => line.Kind switch
    {
        DiffLineKind.Added => $"+{line.Text}",
        DiffLineKind.Removed => $"-{line.Text}",
        _ => $" {line.Text}",
    })];
}
