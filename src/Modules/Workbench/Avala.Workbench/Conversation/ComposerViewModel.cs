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

    string SendHint { get; }

    string Queued { get; }

    string QueuedCaption { get; }

    IAsyncRelayCommand SendCommand { get; }

    IAsyncRelayCommand InterruptCommand { get; }

    IAsyncRelayCommand StopCommand { get; }

    IRelayCommand WithdrawCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ComposerViewModel : IComposerViewModel
{
    private readonly JobId job;
    private readonly JobSteering steering;

    public ComposerViewModel(JobId job, JobSteering steering)
    {
        this.job = job;
        this.steering = steering;
        Draft = string.Empty;
        Error = string.Empty;
        Queued = string.Empty;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Draft { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AcceptsMessages), nameof(Placeholder), nameof(SendHint), nameof(QueuedCaption))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(InterruptCommand), nameof(StopCommand))]
    public partial JobStatus Status { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placeholder), nameof(SendHint))]
    public partial bool TakesMessagesMidTurn { get; private set; }

    [ObservableProperty]
    public partial string Error { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WithdrawCommand))]
    public partial string Queued { get; private set; }

    public bool AcceptsMessages => Status.AcceptsMessages || Status.IsWorking;

    public string Placeholder => ConversationPhrases.Placeholder(Status, TakesMessagesMidTurn);

    public string SendHint => ConversationPhrases.SendHint(Status, TakesMessagesMidTurn);

    public string QueuedCaption => ConversationPhrases.QueuedCaption(Status);

    public void Track(BoardJob shown)
    {
        Status = shown.Status;
        TakesMessagesMidTurn = shown.TakesMessagesMidTurn;
        ShowQueued();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        var sent = await steering.SendAsync(job, Status, Draft.Trim(), cancellationToken);
        Draft = sent.IsSuccess ? string.Empty : Draft;
        Error = sent.Match(_ => string.Empty, ConversationPhrases.Rejection);
        ShowQueued();
    }

    [RelayCommand(CanExecute = nameof(CanInterrupt))]
    private async Task InterruptAsync(CancellationToken cancellationToken) =>
        Report(await steering.InterruptAsync(job, cancellationToken));

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync(CancellationToken cancellationToken) =>
        Report(await steering.StopAsync(job, cancellationToken));

    [RelayCommand(CanExecute = nameof(CanWithdraw))]
    private void Withdraw()
    {
        steering.Withdraw(job);
        ShowQueued();
    }

    private bool CanSend() => AcceptsMessages && !string.IsNullOrWhiteSpace(Draft);

    private bool CanInterrupt() => Status.CanBeInterrupted;

    private bool CanStop() => Status.CanBeStopped;

    private bool CanWithdraw() => Queued.Length > 0;

    private void ShowQueued() => Queued = steering.Queued(job).Match(message => message, () => string.Empty);

    private void Report(Result<JobId, JobRejection> outcome) =>
        Error = outcome.Match(_ => string.Empty, ConversationPhrases.Rejection);
}
