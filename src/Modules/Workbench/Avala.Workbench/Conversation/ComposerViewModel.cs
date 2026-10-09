using Avala.Jobs.Contracts;
using Avala.Sdk;
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

    IAsyncRelayCommand SendCommand { get; }

    IAsyncRelayCommand InterruptCommand { get; }

    IAsyncRelayCommand StopCommand { get; }
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
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial string Draft { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AcceptsMessages), nameof(Placeholder))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(InterruptCommand), nameof(StopCommand))]
    public partial JobStatus Status { get; private set; }

    [ObservableProperty]
    public partial string Error { get; private set; }

    public bool AcceptsMessages => Status.AcceptsMessages;

    public string Placeholder => ConversationPhrases.Placeholder(Status);

    public void Track(JobStatus status) => Status = status;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync(CancellationToken cancellationToken)
    {
        var sent = await steering.SendAsync(job, Draft.Trim(), cancellationToken);
        Draft = sent.IsSuccess ? string.Empty : Draft;
        Report(sent);
    }

    [RelayCommand(CanExecute = nameof(CanInterrupt))]
    private async Task InterruptAsync(CancellationToken cancellationToken) =>
        Report(await steering.InterruptAsync(job, cancellationToken));

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync(CancellationToken cancellationToken) =>
        Report(await steering.StopAsync(job, cancellationToken));

    private bool CanSend() => Status.AcceptsMessages && !string.IsNullOrWhiteSpace(Draft);

    private bool CanInterrupt() => Status.CanBeInterrupted;

    private bool CanStop() => Status.CanBeStopped;

    private void Report(Result<JobId, JobRejection> outcome) =>
        Error = outcome.Match(_ => string.Empty, ConversationPhrases.Rejection);
}
