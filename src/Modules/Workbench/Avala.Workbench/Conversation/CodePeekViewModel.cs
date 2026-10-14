using System.Text;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Reviewing;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface ICodePeekViewModel
{
    string Name { get; }

    string Path { get; }

    string Code { get; }

    string Language { get; }

    int Line { get; }

    bool IsOpen { get; }

    IAsyncRelayCommand CloseCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class CodePeekViewModel(JobId job, WorkedFiles files) : ICodePeekViewModel
{
    public const int Limit = 2 * 1024 * 1024;

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Path { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Code { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Language { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int Line { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CloseCommand))]
    public partial bool IsOpen { get; private set; }

    public async Task<string> ShowAsync(string path, int line, CancellationToken cancellationToken)
    {
        var name = System.IO.Path.GetFileName(path);
        var read = await files.ReadAsync(job, path, Limit, cancellationToken);
        var bytes = read.Match(found => found.Match(content => content, () => []), _ => []);

        if (bytes.Length == 0)
        {
            return read.Match(_ => ConversationPhrases.NotInWorktree(path), failure => failure == Avala.Workspaces.Contracts.WorkspaceFailure.FileTooLarge ? ConversationPhrases.TooLargeToPreview(name) : ConversationPhrases.NotInWorktree(path));
        }

        if (Array.IndexOf(bytes, (byte)0) >= 0)
        {
            return ConversationPhrases.NotText(name);
        }

        Name = name;
        Path = path;
        Code = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
        Language = System.IO.Path.GetExtension(path).TrimStart('.');
        Line = line;
        IsOpen = true;

        return string.Empty;
    }

    [RelayCommand(CanExecute = nameof(IsOpen))]
    private Task CloseAsync()
    {
        IsOpen = false;
        Code = string.Empty;

        return Task.CompletedTask;
    }
}
