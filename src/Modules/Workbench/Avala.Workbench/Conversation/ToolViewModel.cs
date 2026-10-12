using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IToolViewModel
{
    ItemKind Kind { get; }

    string Title { get; }

    bool IsCode { get; }

    string Input { get; }

    string Output { get; }

    bool IsRunning { get; }

    bool Failed { get; }

    string Outcome { get; }

    bool ShowsOutcome { get; }

    bool IsExpanded { get; }

    bool IsCopied { get; }

    bool ShowsChanges { get; }

    IFileChangesViewModel Changes { get; }

    IAsyncRelayCommand ToggleCommand { get; }

    IAsyncRelayCommand CopyCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ToolViewModel : IToolViewModel, ITimelineItem
{
    private readonly IClipboard clipboard;
    private readonly FileChangesViewModel changes;

    public ToolViewModel(ToolEntry entry, IClipboard clipboard, FileViews files)
    {
        this.clipboard = clipboard;
        changes = files.Changes(entry.EditedPath.Match(path => path, () => string.Empty));
        Kind = entry.Kind;
        Title = string.Empty;
        Output = string.Empty;
        Outcome = string.Empty;
        Update(entry);
    }

    public ItemKind Kind { get; }

    [ObservableProperty]
    public partial string Title { get; private set; }

    [ObservableProperty]
    public partial bool IsCode { get; private set; }

    [ObservableProperty]
    public partial string Input { get; private set; } = string.Empty;

    public bool IsShown => true;

    [ObservableProperty]
    public partial string Output { get; private set; }

    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    [ObservableProperty]
    public partial bool Failed { get; private set; }

    [ObservableProperty]
    public partial string Outcome { get; private set; }

    [ObservableProperty]
    public partial bool ShowsOutcome { get; private set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; private set; }

    [ObservableProperty]
    public partial bool IsCopied { get; private set; }

    [ObservableProperty]
    public partial bool ShowsChanges { get; private set; }

    public IFileChangesViewModel Changes => changes;

    public void Update(ITimelineEntry entry)
    {
        if (entry is ToolEntry tool)
        {
            Input = tool.Input.Match(input => input, () => string.Empty);
            Title = tool.WaitsFor.IsSome
                ? ConversationPhrases.Waited(tool.Awaited, tool.Outcome.IsSome)
                : tool.Drafted.Bind(received => tool.Outcome.IsNone ? Option<int>.Some(received) : Option<int>.None).Match(
                    received => ConversationPhrases.Drafting(tool.Title, received),
                    () => tool.Delegation.Match(ConversationPhrases.Delegated, () => Presenting.CommandPhrases.Title(tool.Kind, tool.Title, Input)));
            IsCode = tool.WaitsFor.IsNone && tool.Delegation.IsNone;
            Output = tool.Output;
            IsRunning = tool.Outcome.IsNone;
            var ended = tool.Outcome.Match(outcome => outcome != ItemOutcome.Succeeded, () => false);
            Failed = ended && !tool.Denied;
            ShowsOutcome = ended;
            Outcome = tool.Outcome.Match(outcome => ConversationPhrases.Outcome(outcome, tool.Denied), () => "running");
            ShowsChanges = changes.Path.Length > 0 && tool.Outcome == Option<ItemOutcome>.Some(ItemOutcome.Succeeded);
        }
    }

    [RelayCommand]
    private async Task ToggleAsync(CancellationToken cancellationToken)
    {
        if (ShowsChanges)
        {
            await changes.ShowCommand.ExecuteAsync(null);
        }
        else
        {
            IsExpanded = !IsExpanded;
        }
    }

    [RelayCommand]
    private async Task CopyAsync(CancellationToken cancellationToken)
    {
        IsCopied = false;
        IsCopied = await clipboard.CopyAsync(Output, cancellationToken);
    }
}
