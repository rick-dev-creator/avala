using System.Collections.ObjectModel;
using Avala.Jobs.Contracts;
using Avala.Workbench.Review;
using Avala.Workbench.Reviewing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IFileChangesViewModel
{
    string Path { get; }

    IReadOnlyList<IHunkViewModel> Hunks { get; }

    string Note { get; }

    bool IsOpen { get; set; }

    IAsyncRelayCommand ShowCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FileChangesViewModel : IFileChangesViewModel
{
    private readonly JobId job;
    private readonly WorkedFiles files;
    private readonly ObservableCollection<IHunkViewModel> hunks = [];

    public FileChangesViewModel(JobId job, string path, WorkedFiles files)
    {
        this.job = job;
        this.files = files;
        Path = path;
        Note = string.Empty;
    }

    public string Path { get; }

    public IReadOnlyList<IHunkViewModel> Hunks => hunks;

    [ObservableProperty]
    public partial string Note { get; private set; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [RelayCommand]
    private async Task ShowAsync(CancellationToken cancellationToken)
    {
        var diff = await files.ChangesAsync(job, Path, cancellationToken);
        hunks.Clear();
        Note = diff.Match(
            found =>
            {
                foreach (var hunk in found.Hunks)
                {
                    hunks.Add(new HunkViewModel(hunk));
                }

                return found.Binary ? ConversationPhrases.BinaryChanges : found.Hunks.Count == 0 ? ConversationPhrases.NoChanges : string.Empty;
            },
            _ => ConversationPhrases.ChangesUnreadable);
        IsOpen = true;
    }
}
