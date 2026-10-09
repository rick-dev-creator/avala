using System.Collections.ObjectModel;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Reviewing;
using Avala.Workbench.Steering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Review;

internal interface IReviewViewModel
{
    JobId Job { get; }

    JobStatus Status { get; }

    bool IsLoaded { get; }

    string Verdict { get; }

    bool HasExceptions { get; }

    IReadOnlyList<IReviewExceptionViewModel> Exceptions { get; }

    string Quiet { get; }

    string Changes { get; }

    IReadOnlyList<IChangedFileViewModel> Files { get; }

    string Feedback { get; set; }

    string Outcome { get; }

    IReadOnlyList<string> Conflicts { get; }

    bool ConfirmingDiscard { get; }

    IAsyncRelayCommand ApproveCommand { get; }

    IAsyncRelayCommand SendBackCommand { get; }

    IRelayCommand RequestDiscardCommand { get; }

    IRelayCommand CancelDiscardCommand { get; }

    IAsyncRelayCommand ConfirmDiscardCommand { get; }
}

internal interface IReviewExceptionViewModel
{
    string Title { get; }

    string Detail { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ReviewViewModel : IReviewViewModel, IPresentation
{
    private readonly ReviewReader reader;
    private readonly ReviewDesk desk;
    private readonly IUiDispatcher ui;
    private readonly ObservableCollection<ReviewExceptionViewModel> exceptions = [];
    private readonly ObservableCollection<ChangedFileViewModel> files = [];

    public ReviewViewModel(JobId job, ReviewReader reader, ReviewDesk desk, IUiDispatcher ui)
    {
        Job = job;
        this.reader = reader;
        this.desk = desk;
        this.ui = ui;
        Verdict = string.Empty;
        Quiet = string.Empty;
        Changes = string.Empty;
        Feedback = string.Empty;
        Outcome = string.Empty;
        Conflicts = [];
    }

    public JobId Job { get; }

    public int Requested { get; private set; } = -1;

    public long Revision { get; private set; }

    public event EventHandler<Presented>? Presented;

    public IReadOnlyList<IReviewExceptionViewModel> Exceptions => exceptions;

    public IReadOnlyList<IChangedFileViewModel> Files => files;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand), nameof(SendBackCommand), nameof(RequestDiscardCommand), nameof(ConfirmDiscardCommand))]
    public partial JobStatus Status { get; private set; }

    [ObservableProperty]
    public partial bool IsLoaded { get; private set; }

    [ObservableProperty]
    public partial string Verdict { get; private set; }

    [ObservableProperty]
    public partial bool HasExceptions { get; private set; }

    [ObservableProperty]
    public partial string Quiet { get; private set; }

    [ObservableProperty]
    public partial string Changes { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendBackCommand))]
    public partial string Feedback { get; set; }

    [ObservableProperty]
    public partial string Outcome { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> Conflicts { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RequestDiscardCommand), nameof(ConfirmDiscardCommand), nameof(CancelDiscardCommand))]
    public partial bool ConfirmingDiscard { get; private set; }

    public void Track(JobStatus status) => Status = status;

    public Func<CancellationToken, Task> Request(int revision)
    {
        Requested = revision;

        return cancellationToken => LoadAsync(revision, cancellationToken);
    }

    private async Task LoadAsync(int revision, CancellationToken cancellationToken)
    {
        var facts = await reader.ReadAsync(Job, cancellationToken);
        await ui.InvokeAsync(
            () =>
            {
                if (!cancellationToken.IsCancellationRequested && revision == Requested)
                {
                    Show(facts);
                    Revision++;
                    Presented?.Invoke(this, new Presented(Revision));
                }
            },
            cancellationToken);
    }

    private void Show(ReviewFacts facts)
    {
        Verdict = facts.Evidence.Match(evidence => ReviewPhrases.Verdict(ReviewExceptions.VerdictOf(evidence)), () => "No evidence for this job");
        Quiet = facts.Evidence.Match(evidence => ReviewPhrases.Quiet(evidence, facts.Usage), () => string.Empty);
        exceptions.ShowOnly(facts.Evidence.Match(ReviewExceptions.Of, () => []).Select(exception => new ReviewExceptionViewModel(exception)));
        HasExceptions = exceptions.Count > 0;
        Changes = ReviewPhrases.Changes(facts.Diff);
        var kept = files.ToDictionary(file => (file.Path, file.Kind, file.Counts));
        files.ShowOnly(facts.Diff.Match(
            diff => diff.Files.Select(file => kept.GetValueOrDefault((file.Path, file.Kind, ReviewPhrases.Counts(file))) ?? new ChangedFileViewModel(file, diff.Workspace, reader)),
            _ => []));
        IsLoaded = true;
    }

    [RelayCommand(CanExecute = nameof(AwaitsReview))]
    private async Task ApproveAsync(CancellationToken cancellationToken)
    {
        var attempt = await desk.ApproveAsync(Job, cancellationToken);
        Outcome = ReviewPhrases.Approval(attempt);
        Conflicts = attempt.Conflicts;
    }

    [RelayCommand(CanExecute = nameof(CanSendBack))]
    private async Task SendBackAsync(CancellationToken cancellationToken)
    {
        var sent = await desk.SendBackAsync(Job, Feedback.Trim(), cancellationToken);
        Feedback = sent.IsSuccess ? string.Empty : Feedback;
        Outcome = sent.Match(_ => "Sent back for another round", ReviewPhrases.Refusal);
    }

    [RelayCommand(CanExecute = nameof(CanRequestDiscard))]
    private void RequestDiscard() => ConfirmingDiscard = true;

    [RelayCommand(CanExecute = nameof(ConfirmingDiscard))]
    private void CancelDiscard() => ConfirmingDiscard = false;

    [RelayCommand(CanExecute = nameof(CanConfirmDiscard))]
    private async Task ConfirmDiscardAsync(CancellationToken cancellationToken)
    {
        var discarded = await desk.DiscardAsync(Job, cancellationToken);
        ConfirmingDiscard = false;
        Outcome = discarded.Match(_ => "Discarded", ReviewPhrases.Refusal);
    }

    private bool AwaitsReview() => Status == JobStatus.AwaitingReview;

    private bool CanSendBack() => AwaitsReview() && !string.IsNullOrWhiteSpace(Feedback);

    private bool CanRequestDiscard() => Status.CanBeDiscarded && !ConfirmingDiscard;

    private bool CanConfirmDiscard() => Status.CanBeDiscarded && ConfirmingDiscard;
}

internal sealed class ReviewExceptionViewModel : IReviewExceptionViewModel
{
    public ReviewExceptionViewModel(IReviewException exception) => (Title, Detail) = ReviewPhrases.Exception(exception);

    public string Title { get; }

    public string Detail { get; }
}
