using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Steering;
using Avala.Workbench.Harnesses;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Conversation;

internal interface IComposerViewModel
{
    string Draft { get; set; }

    JobStatus Status { get; }

    string Error { get; }

    string Placeholder { get; }

    bool AcceptsMessages { get; }

    bool CanHalt { get; }

    bool ShowsStop { get; }

    bool ShowsSend { get; }

    bool SendsWithEnter { get; }

    string SendHint { get; }

    string Queued { get; }

    string QueuedCaption { get; }

    IAsyncRelayCommand SendCommand { get; }

    IAsyncRelayCommand EnterCommand { get; }

    IAsyncRelayCommand InterruptCommand { get; }

    IAsyncRelayCommand StopCommand { get; }

    IRelayCommand WithdrawCommand { get; }

    int Edits { get; }

    IHarnessChoiceViewModel Harness { get; }

    string ColdNote { get; }

    bool CanCompact { get; }

    IAsyncRelayCommand CompactCommand { get; }

    IAttachmentTrayViewModel Attachments { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ComposerViewModel : IComposerViewModel
{
    private readonly JobSteering steering;
    private readonly SendKeys keys;
    private readonly AttachmentTrayViewModel attachments;

    private readonly JobHarness harness;

    public ComposerViewModel(JobId job, JobSteering steering, ComposerTools tools, JobHarness harness)
    {
        Job = job;
        this.harness = harness;
        this.steering = steering;
        keys = tools.Keys;
        attachments = tools.Attachments;
        SendsWithEnter = keys.WithEnter;
        harness.Loaded += (_, _) =>
        {
            ShowCold();
            attachments.Accept(harness.Attachments);
        };
        Draft = string.Empty;
        Error = string.Empty;
        Queued = string.Empty;
    }

    public JobId Job { get; }

    public IHarnessChoiceViewModel Harness => harness.Chip;

    public JobHarness Driver => harness;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStop), nameof(ShowsSend))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Draft { get; set; }

    [ObservableProperty]
    public partial string ColdNote { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompactCommand))]
    public partial bool CanCompact { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AcceptsMessages), nameof(CanHalt), nameof(ShowsStop), nameof(ShowsSend), nameof(Placeholder), nameof(SendHint), nameof(QueuedCaption))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(InterruptCommand), nameof(StopCommand))]
    public partial JobStatus Status { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placeholder), nameof(SendHint))]
    public partial bool TakesMessagesMidTurn { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    public partial Option<PullRequestWatchState> Watch { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    public partial Option<HoldReason> Hold { get; private set; }

    [ObservableProperty]
    public partial string Error { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WithdrawCommand))]
    public partial string Queued { get; private set; }

    public bool AcceptsMessages => Status.AcceptsMessages || Status.IsWorking;

    public bool CanHalt => Status.CanBeInterrupted;

    public bool ShowsStop => CanHalt && string.IsNullOrWhiteSpace(Draft);

    public bool ShowsSend => AcceptsMessages && !ShowsStop;

    [ObservableProperty]
    public partial bool SendsWithEnter { get; private set; }

    [ObservableProperty]
    public partial int Edits { get; private set; }

    public string Placeholder => ConversationPhrases.Placeholder(Status, TakesMessagesMidTurn, Watch, Hold);

    public string SendHint => ConversationPhrases.SendHint(Status, TakesMessagesMidTurn);

    public string QueuedCaption => ConversationPhrases.QueuedCaption(Status);

    public void Track(BoardJob shown)
    {
        Status = shown.Status;
        TakesMessagesMidTurn = shown.TakesMessagesMidTurn;
        Watch = shown.PullRequest;
        Hold = shown.Hold;
        SendsWithEnter = keys.WithEnter;
        ShowQueued();
        harness.Track(shown);
        ShowCold();
    }

    partial void OnDraftChanged(string value) => ShowCold();

    private void ShowCold()
    {
        var cold = Status.AcceptsMessages && !Status.IsWorking ? harness.Cold() : Option<ColdConversation>.None;
        ColdNote = cold.Match(ConversationPhrases.Cold, () => string.Empty);
        CanCompact = cold.Match(found => found.Compact.IsSome, () => false);
    }

    [RelayCommand(CanExecute = nameof(CanCompact))]
    private async Task CompactAsync(CancellationToken cancellationToken)
    {
        var message = harness.Cold().Bind(found => found.Compact).Match(found => found, () => string.Empty);

        if (message.Length > 0)
        {
            var sent = await steering.SendAsync(Job, Status, message, cancellationToken);
            Error = sent.Match(_ => string.Empty, ConversationPhrases.Rejection);
        }
    }

    public void Edit(string text)
    {
        Draft = text;
        Edits++;
    }

    [RelayCommand]
    private async Task ResendAsync(string? text, CancellationToken cancellationToken)
    {
        var sent = await steering.SendAsync(Job, Status, text ?? string.Empty, cancellationToken);
        Error = sent.Match(_ => string.Empty, ConversationPhrases.Rejection);
        ShowQueued();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        var sent = await steering.SendAsync(Job, Status, new JobMessage(Draft.Trim()) { Attachments = attachments.Attached }, cancellationToken);
        Draft = sent.IsSuccess ? string.Empty : Draft;

        if (sent.IsSuccess)
        {
            attachments.Clear();
        }

        Error = sent.Match(_ => string.Empty, ConversationPhrases.Rejection);
        ShowQueued();
    }

    [RelayCommand(CanExecute = nameof(SendsOnEnter))]
    private Task EnterAsync(CancellationToken cancellationToken) =>
        CanSend() ? SendAsync(cancellationToken) : Task.CompletedTask;

    private bool SendsOnEnter() => keys.WithEnter;

    [RelayCommand(CanExecute = nameof(CanInterrupt))]
    private async Task InterruptAsync(CancellationToken cancellationToken) =>
        Report(await steering.InterruptAsync(Job, cancellationToken));

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync(CancellationToken cancellationToken) =>
        Report(await steering.StopAsync(Job, cancellationToken));

    [RelayCommand(CanExecute = nameof(CanWithdraw))]
    private void Withdraw()
    {
        steering.Withdraw(Job);
        ShowQueued();
    }

    private bool CanSend() => AcceptsMessages && !string.IsNullOrWhiteSpace(Draft);

    private bool CanInterrupt() => Status.CanBeInterrupted;

    private bool CanStop() => Status.CanBeStopped;

    private bool CanWithdraw() => Queued.Length > 0;

    public IAttachmentTrayViewModel Attachments => attachments;

    private void ShowQueued() => Queued = steering.Queued(Job).Match(message => message, () => string.Empty);

    private void Report(Result<JobId, JobRejection> outcome) =>
        Error = outcome.Match(_ => string.Empty, ConversationPhrases.Rejection);
}
