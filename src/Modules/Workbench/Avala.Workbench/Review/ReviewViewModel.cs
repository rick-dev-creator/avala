using System.Collections.ObjectModel;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Reviewing;
using Avala.Workbench.Steering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Review;

[INotifyPropertyChanged]
internal sealed partial class ReviewViewModel
{
    private readonly ReviewReader reader;
    private readonly ReviewDesk desk;
    private readonly IUiDispatcher ui;

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

    public ObservableCollection<ReviewExceptionViewModel> Exceptions { get; } = [];

    public ObservableCollection<ChangedFileViewModel> Files { get; } = [];

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
                }
            },
            cancellationToken);
    }

    private void Show(ReviewFacts facts)
    {
        Verdict = facts.Evidence.Match(evidence => ReviewPhrases.Verdict(ReviewExceptions.VerdictOf(evidence)), () => "No evidence for this job");
        Quiet = facts.Evidence.Match(evidence => ReviewPhrases.Quiet(evidence, facts.Usage), () => string.Empty);
        Exceptions.Clear();

        foreach (var exception in facts.Evidence.Match(ReviewExceptions.Of, () => []))
        {
            Exceptions.Add(new ReviewExceptionViewModel(exception));
        }

        HasExceptions = Exceptions.Count > 0;
        Changes = ReviewPhrases.Changes(facts.Diff);
        Files.Clear();

        foreach (var file in facts.Diff.Match(diff => diff.Files.Select(file => new ChangedFileViewModel(file, diff.Workspace, reader)), _ => []))
        {
            Files.Add(file);
        }

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

internal sealed class ReviewExceptionViewModel
{
    public ReviewExceptionViewModel(IReviewException exception) => (Title, Detail) = ReviewPhrases.Exception(exception);

    public string Title { get; }

    public string Detail { get; }
}
