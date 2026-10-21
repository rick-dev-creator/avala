using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Workbench.Review;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IToolViewModel
{
    FindMark Found { get; }

    ItemKind Kind { get; }

    string Title { get; }

    bool IsCode { get; }

    string Input { get; }

    string InputLanguage { get; }

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

    string ShownOutput { get; }

    int HiddenLines { get; }

    IReadOnlyList<string> Problems { get; }

    IRelayCommand ShowAllOutputCommand { get; }

    bool ShowsAllChanges { get; }

    bool ShowsCopy { get; }

    IEditDiffViewModel Edit { get; }

    bool ShowsPreview { get; }

    IFilePreviewViewModel Preview { get; }

    IAsyncRelayCommand ToggleCommand { get; }

    IAsyncRelayCommand CopyCommand { get; }

    IAsyncRelayCommand ShowChangesCommand { get; }

    bool InRun { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ToolViewModel : IToolViewModel, ITimelineItem, ISearchable, IRunMember
{
    private readonly IClipboard clipboard;
    private readonly FileChangesViewModel changes;
    private readonly FilePreviewViewModel preview;
    private readonly string path;
    private bool showsAll;

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

    public string InputLanguage => Kind == ItemKind.Command ? "shellscript" : string.Empty;

    public bool IsShown => true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsNoOutput), nameof(ShowsCopy))]
    public partial string Output { get; private set; }

    [ObservableProperty]
    public partial string ShownOutput { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ShowAllOutputCommand))]
    public partial int HiddenLines { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> Problems { get; private set; } = [];

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

    public bool StandsAlone => ShowsOutcome || !IsCode || Kind == ItemKind.Subagent;

    [ObservableProperty]
    public partial IEditDiffViewModel Edit { get; private set; } = EditDiffViewModel.Of(string.Empty, string.Empty);

    [ObservableProperty]
    public partial bool ShowsPreview { get; private set; }

    public IFilePreviewViewModel Preview => preview;

    [ObservableProperty]
    public partial FindMark Found { get; set; }

    [ObservableProperty]
    public partial bool InRun { get; set; }

    public string SearchText => $"{Title}\n{Output}";

    public bool Unfold()
    {
        var folded = !IsExpanded;
        IsExpanded = true;

        return folded;
    }

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
            Failed = ended && !tool.Denied && !ConversationPhrases.Stopped(tool.Kind, tool.Outcome);
            ShowsOutcome = ended;
            Outcome = tool.Outcome.Match(outcome => ConversationPhrases.Outcome(tool.Kind, outcome, tool.Denied), () => "running");
            Fold();
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

    [RelayCommand(CanExecute = nameof(HasHiddenLines))]
    private void ShowAllOutput()
    {
        showsAll = true;
        Fold();
    }

    private bool HasHiddenLines() => HiddenLines > 0;

    private void Fold()
    {
        var folded = OutputFold.Of(Output, !IsRunning, Failed, showsAll);
        ShownOutput = folded.Shown;
        HiddenLines = folded.Hidden;
        Problems = Problems.SequenceEqual(folded.Problems) ? Problems : folded.Problems;
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
