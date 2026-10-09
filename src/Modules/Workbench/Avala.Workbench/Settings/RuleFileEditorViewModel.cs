using Avala.Sdk;
using Avala.Workbench.Machine;
using Avala.Workbench.RepositoryRules;
using Avala.Workspaces.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface IRuleFileEditorViewModel
{
    bool IsOpen { get; }

    string Path { get; }

    string Content { get; set; }

    string Note { get; }

    string Error { get; }

    IAsyncRelayCommand SaveCommand { get; }

    IRelayCommand CancelCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class RuleFileEditorViewModel(RuleFileEditing editing) : IRuleFileEditorViewModel
{
    private string repository = string.Empty;

    public event EventHandler<string>? Saved;

    [ObservableProperty]
    public partial bool IsOpen { get; private set; }

    [ObservableProperty]
    public partial string Path { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Content { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Note { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    public bool CanEdit(string path) => editing.CanEdit(path);

    public async Task OpenAsync(string shown, string path, CancellationToken cancellationToken)
    {
        var read = await editing.ReadAsync(shown, path, SettingsFiles.TemplateOf(path), cancellationToken);
        repository = shown;
        Path = path;
        Error = read.Match(_ => string.Empty, failure => RuleFilePhrases.Unread(path, failure));
        Content = read.Match(draft => draft.Content, _ => string.Empty);
        Note = read.Match(draft => RuleFilePhrases.Note(path, draft.Exists), _ => string.Empty);
        IsOpen = read.IsSuccess;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var rejected = editing.Rejection(Path, Content);

        if (rejected.IsSome)
        {
            Error = rejected.Match(rejection => RuleFilePhrases.Rejected(Path, rejection), () => string.Empty);
            return;
        }

        var saved = await editing.WriteAsync(repository, Path, Content, cancellationToken);
        Error = saved.Match(_ => string.Empty, failure => RuleFilePhrases.Unread(Path, failure));

        if (saved.IsSuccess)
        {
            IsOpen = false;
            Saved?.Invoke(this, Path);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        IsOpen = false;
        Error = string.Empty;
    }

    private bool CanSave() => IsOpen && Content.Trim().Length > 0;

    partial void OnIsOpenChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
}

internal static class RuleFilePhrases
{
    public static string Note(string path, bool exists) =>
        (exists ? string.Empty : $"{path} does not exist yet: this is a minimal template. ")
        + $"Saving writes it in the repository's working tree after checking it as jobs do. Jobs read {path} from the commit they start from, so the change applies to new jobs once you commit it.";

    public static string Saved(string path) =>
        $"Saved {path} in the repository's working tree. It applies to new jobs once you commit it.";

    public static string Rejected(string path, RuleFileRejection rejection) =>
        $"Not saved: jobs would reject {path} ({rejection.Error}, from {rejection.Module}). Fix it and save again.";

    public static string Unread(string path, WorkspaceFailure failure) => Failure(path, failure);

    private static string Failure(string path, WorkspaceFailure failure) => failure switch
    {
        WorkspaceFailure.NotAGitRepository => "This folder is not a git repository.",
        WorkspaceFailure.FileTooLarge => $"{path} is larger than 64 KiB.",
        WorkspaceFailure.FileUnreadable => $"{path} could not be read.",
        WorkspaceFailure.FileUnwritable => $"{path} could not be written.",
        WorkspaceFailure.OutsideRepository => $"{path} is outside the repository.",
        _ => $"{path} could not be reached ({failure}).",
    };
}
