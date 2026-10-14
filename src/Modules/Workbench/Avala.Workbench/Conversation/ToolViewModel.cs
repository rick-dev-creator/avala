using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Workbench.Review;
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

    bool ShowsEdit { get; }

    bool ShowsInput { get; }

    bool ShowsNoOutput { get; }

    bool ShowsAllChanges { get; }

    bool ShowsCopy { get; }

    IHunkViewModel Edit { get; }

    bool ShowsPreview { get; }

    IFilePreviewViewModel Preview { get; }

    IAsyncRelayCommand ToggleCommand { get; }

    IAsyncRelayCommand CopyCommand { get; }

    IAsyncRelayCommand ShowChangesCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ToolViewModel : IToolViewModel, ITimelineItem
{
    private readonly IClipboard clipboard;
    private readonly FileChangesViewModel changes;
    private readonly FilePreviewViewModel preview;
    private readonly string path;

    public ToolViewModel(ToolEntry entry, IClipboard clipboard, FileViews files)
    {
        this.clipboard = clipboard;
        path = entry.EditedPath.Match(found => found, () => string.Empty);
        changes = files.Changes(path);
        preview = files.Preview(path);
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
    [NotifyPropertyChangedFor(nameof(ShowsInput))]
    public partial string Input { get; private set; } = string.Empty;

    public bool IsShown => true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNoOutput), nameof(ShowsCopy))]
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
    [NotifyPropertyChangedFor(nameof(ShowsAllChanges), nameof(ShowsCopy))]
    public partial bool ShowsChanges { get; private set; }

    public IFileChangesViewModel Changes => changes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsInput), nameof(ShowsNoOutput), nameof(ShowsAllChanges), nameof(ShowsCopy))]
    public partial bool ShowsEdit { get; private set; }

    public bool ShowsInput => Input.Length > 0 && !ShowsEdit;

    public bool ShowsNoOutput => Output.Length == 0 && !ShowsEdit;

    public bool ShowsAllChanges => ShowsEdit && ShowsChanges;

    public bool ShowsCopy => Output.Length > 0 && !ShowsAllChanges;

    [ObservableProperty]
    public partial IHunkViewModel Edit { get; private set; } = EditDiffViewModel.Of(string.Empty, string.Empty);

    [ObservableProperty]
    public partial bool ShowsPreview { get; private set; }

    public IFilePreviewViewModel Preview => preview;

    public void Update(ITimelineEntry entry)
    {
        if (entry is ToolEntry tool)
        {
            Input = tool.Input.Match(input => input, () => string.Empty);
            ShowsEdit = Kind == ItemKind.FileEdit && Input.Length > 0 && tool.Outcome.IsSome;

            if (ShowsEdit && IsExpanded)
            {
                Edit = EditDiffViewModel.Of(path, Input);
            }

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
            Reveal(ShowsChanges && preview.Kind != PreviewKind.None);
        }
    }

    private void Reveal(bool shows)
    {
        if (shows && !ShowsPreview && preview.Kind is PreviewKind.Image or PreviewKind.Svg)
        {
            preview.LoadCommand.Execute(null);
        }

        ShowsPreview = shows;
    }

    [RelayCommand]
    private async Task ToggleAsync(CancellationToken cancellationToken)
    {
        if (ShowsChanges && !ShowsEdit)
        {
            await changes.ShowCommand.ExecuteAsync(null);
        }
        else
        {
            if (ShowsEdit && !IsExpanded)
            {
                Edit = EditDiffViewModel.Of(path, Input);
            }

            IsExpanded = !IsExpanded;
        }
    }

    [RelayCommand]
    private Task ShowChangesAsync(CancellationToken cancellationToken) => changes.ShowCommand.ExecuteAsync(null);

    [RelayCommand]
    private async Task CopyAsync(CancellationToken cancellationToken)
    {
        IsCopied = false;
        IsCopied = await clipboard.CopyAsync(Output, cancellationToken);
    }
}
