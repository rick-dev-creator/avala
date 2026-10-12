using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Steering;
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
}

[INotifyPropertyChanged]
internal sealed partial class ComposerViewModel : IComposerViewModel
{
    private readonly JobSteering steering;
    private readonly SendKeys keys;

    public ComposerViewModel(JobId job, JobSteering steering, SendKeys keys)
    {
        Job = job;
        this.steering = steering;
        this.keys = keys;
        SendsWithEnter = keys.WithEnter;
        Draft = string.Empty;
        Error = string.Empty;
        Queued = string.Empty;
    }

    public JobId Job { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStop), nameof(ShowsSend))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Draft { get; set; }

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

    public string Placeholder => ConversationPhrases.Placeholder(Status, TakesMessagesMidTurn, Watch);

    public string SendHint => ConversationPhrases.SendHint(Status, TakesMessagesMidTurn);

    public string QueuedCaption => ConversationPhrases.QueuedCaption(Status);

    public void Track(BoardJob shown)
    {
        Status = shown.Status;
        TakesMessagesMidTurn = shown.TakesMessagesMidTurn;
        Watch = shown.PullRequest;
        SendsWithEnter = keys.WithEnter;
        ShowQueued();
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
        var sent = await steering.SendAsync(Job, Status, Draft.Trim(), cancellationToken);
        Draft = sent.IsSuccess ? string.Empty : Draft;
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

    private void ShowQueued() => Queued = steering.Queued(Job).Match(message => message, () => string.Empty);

    private void Report(Result<JobId, JobRejection> outcome) =>
        Error = outcome.Match(_ => string.Empty, ConversationPhrases.Rejection);
}
