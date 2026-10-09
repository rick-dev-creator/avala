using System.Collections.ObjectModel;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Workbench.Presenting;
using Avala.Workbench.Reviewing;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Steering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Review;

internal interface IReviewViewModel
{
    JobId Job { get; }

    JobStatus Status { get; }

    string Heading { get; }

    string Title { get; }

    string Facts { get; }

    bool IsLoaded { get; }

    string Verdict { get; }

    string Proof { get; }

    bool IsVerified { get; }

    bool HasExceptions { get; }

    IReadOnlyList<IReviewExceptionViewModel> Exceptions { get; }

    string Quiet { get; }

    string Changes { get; }

    string Totals { get; }

    IReadOnlyList<IChangedFileViewModel> Files { get; }

    string Feedback { get; set; }

    string Outcome { get; }

    bool IsClosed { get; }

    string Refusal { get; }

    IReadOnlyList<string> Conflicts { get; }

    bool ConfirmingDiscard { get; }

    IAsyncRelayCommand ApproveCommand { get; }

    IAsyncRelayCommand SendBackCommand { get; }

    IRelayCommand RequestDiscardCommand { get; }

    IRelayCommand CancelDiscardCommand { get; }

    IAsyncRelayCommand ConfirmDiscardCommand { get; }

    IRelayCommand CloseCommand { get; }
}

internal interface IReviewExceptionViewModel
{
    string Title { get; }

    string Fact { get; }

    string Detail { get; }

    string Output { get; }

    ExceptionTone Tone { get; }

    bool IsExpanded { get; set; }
}

[INotifyPropertyChanged]
internal sealed partial class ReviewViewModel : IReviewViewModel, IPresentation
{
    private readonly ReviewReader reader;
    private readonly ReviewDesk desk;
    private readonly IUiDispatcher ui;
    private readonly ObservableCollection<ReviewExceptionViewModel> exceptions = [];
    private readonly ObservableCollection<ChangedFileViewModel> files = [];
    private string closedAs = string.Empty;

    public ReviewViewModel(JobId job, ReviewReader reader, ReviewDesk desk, IUiDispatcher ui)
    {
        Job = job;
        this.reader = reader;
        this.desk = desk;
        this.ui = ui;
        Title = string.Empty;
        Facts = string.Empty;
        Verdict = string.Empty;
        Proof = string.Empty;
        Quiet = string.Empty;
        Changes = string.Empty;
        Totals = string.Empty;
        Feedback = string.Empty;
        Outcome = string.Empty;
        Refusal = string.Empty;
        Conflicts = [];
    }

    public JobId Job { get; }

    public int Requested { get; private set; } = -1;

    public long Revision { get; private set; }

    public event EventHandler<Presented>? Presented;

    public event EventHandler? Closed;

    public IReadOnlyList<IReviewExceptionViewModel> Exceptions => exceptions;

    public IReadOnlyList<IChangedFileViewModel> Files => files;

    public string Heading => closedAs.Length > 0 ? closedAs : ReviewPhrases.Heading(Status);

    public bool IsClosed => Outcome.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading))]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand), nameof(SendBackCommand), nameof(RequestDiscardCommand), nameof(ConfirmDiscardCommand))]
    public partial JobStatus Status { get; private set; }

    [ObservableProperty]
    public partial string Title { get; private set; }

    [ObservableProperty]
    public partial string Facts { get; private set; }

    [ObservableProperty]
    public partial bool IsLoaded { get; private set; }

    [ObservableProperty]
    public partial string Verdict { get; private set; }

    [ObservableProperty]
    public partial string Proof { get; private set; }

    [ObservableProperty]
    public partial bool IsVerified { get; private set; }

    [ObservableProperty]
    public partial bool HasExceptions { get; private set; }

    [ObservableProperty]
    public partial string Quiet { get; private set; }

    [ObservableProperty]
    public partial string Changes { get; private set; }

    [ObservableProperty]
    public partial string Totals { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendBackCommand))]
    public partial string Feedback { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Heading), nameof(IsClosed))]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand), nameof(SendBackCommand), nameof(RequestDiscardCommand), nameof(ConfirmDiscardCommand))]
    public partial string Outcome { get; private set; }

    [ObservableProperty]
    public partial string Refusal { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<string> Conflicts { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand), nameof(SendBackCommand), nameof(RequestDiscardCommand), nameof(ConfirmDiscardCommand), nameof(CancelDiscardCommand))]
    public partial bool ConfirmingDiscard { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApproveCommand), nameof(SendBackCommand), nameof(RequestDiscardCommand), nameof(ConfirmDiscardCommand), nameof(CancelDiscardCommand))]
    public partial bool IsActing { get; private set; }

    public void Track(JobStatus status)
    {
        Status = status;

        if (Outcome.Length == 0 && status is JobStatus.Approved or JobStatus.Discarded)
        {
            Outcome = status.ToString();
        }
    }

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
        Title = facts.History.Match(history => FactPhrases.Title(history.Summary.Instruction), () => string.Empty);
        Facts = facts.History.Match(ReviewPhrases.Facts, () => string.Empty);
        var verdict = facts.Evidence.Map(ReviewExceptions.VerdictOf);
        Verdict = verdict.Match(ReviewPhrases.Verdict, () => "No evidence for this job");
        IsVerified = verdict.Match(found => found.Kind == VerdictKind.Verified, () => false);
        Proof = facts.Evidence.Match(ReviewPhrases.Proof, () => string.Empty);
        Quiet = facts.Evidence.Match(evidence => ReviewPhrases.Quiet(evidence, facts.Usage), () => string.Empty);
        ShowExceptions(facts.Evidence.Match(ReviewExceptions.Of, () => []));
        Changes = ReviewPhrases.Changes(facts.Diff);
        Totals = ReviewPhrases.Totals(facts.Diff);
        var kept = files.ToDictionary(file => (file.Path, file.Kind, file.Counts));
        files.ShowOnly(facts.Diff.Match(
            diff => diff.Files.Select(file => kept.GetValueOrDefault((file.Path, file.Kind, ReviewPhrases.Counts(file))) ?? new ChangedFileViewModel(file, diff.Workspace, reader)),
            _ => []));
        IsLoaded = true;
    }

    private void ShowExceptions(IReadOnlyList<IReviewException> found)
    {
        var expanded = exceptions.Where(exception => exception.IsExpanded).Select(exception => (exception.Title, exception.Fact)).ToHashSet();
        var first = !IsLoaded;
        exceptions.ShowOnly(found.Select(exception => new ReviewExceptionViewModel(exception)).Select((exception, position) =>
        {
            exception.IsExpanded = expanded.Contains((exception.Title, exception.Fact)) || (first && position == 0);

            return exception;
        }));
        HasExceptions = exceptions.Count > 0;
    }

    [RelayCommand(CanExecute = nameof(CanApprove))]
    private Task ApproveAsync(CancellationToken cancellationToken) =>
        ActAsync(async () =>
        {
            var attempt = await desk.ApproveAsync(Job, cancellationToken);
            Conflicts = attempt.Conflicts;
            Settle("Approved", attempt.Outcome.Map(ReviewPhrases.Delivered), rejection => rejection == JobRejection.MergeConflict ? ReviewPhrases.Conflicted(attempt.Conflicts) : ReviewPhrases.Refusal(rejection));
        });

    [RelayCommand(CanExecute = nameof(CanSendBack))]
    private Task SendBackAsync(CancellationToken cancellationToken) =>
        ActAsync(async () =>
        {
            var sent = await desk.SendBackAsync(Job, Feedback.Trim(), cancellationToken);
            Feedback = sent.IsSuccess ? string.Empty : Feedback;
            Settle("Sent back", sent.Map(_ => "Sent back with your feedback"), ReviewPhrases.Refusal);
        });

    [RelayCommand(CanExecute = nameof(CanRequestDiscard))]
    private void RequestDiscard() => ConfirmingDiscard = true;

    [RelayCommand(CanExecute = nameof(CanCancelDiscard))]
    private void CancelDiscard() => ConfirmingDiscard = false;

    [RelayCommand(CanExecute = nameof(CanConfirmDiscard))]
    private Task ConfirmDiscardAsync(CancellationToken cancellationToken) =>
        ActAsync(async () =>
        {
            var discarded = await desk.DiscardAsync(Job, cancellationToken);
            ConfirmingDiscard = false;
            Settle("Discarded", discarded.Map(_ => "Discarded"), ReviewPhrases.Refusal);
        });

    [RelayCommand]
    private void Close() => Closed?.Invoke(this, EventArgs.Empty);

    private async Task ActAsync(Func<Task> act)
    {
        if (!IsOpen())
        {
            return;
        }

        IsActing = true;

        try
        {
            await act();
        }
        finally
        {
            IsActing = false;
        }
    }

    private void Settle(string action, Result<string, JobRejection> result, Func<JobRejection, string> refused)
    {
        closedAs = result.IsSuccess ? action : closedAs;
        Refusal = result.Match(_ => string.Empty, refused);
        Outcome = result.Match(done => done, _ => string.Empty);
        Conflicts = result.IsSuccess ? [] : Conflicts;
    }

    private bool IsOpen() => !IsActing && !IsClosed;

    private bool CanApprove() => IsOpen() && !ConfirmingDiscard && Status == JobStatus.AwaitingReview;

    private bool CanSendBack() => CanApprove() && !string.IsNullOrWhiteSpace(Feedback);

    private bool CanRequestDiscard() => IsOpen() && Status.CanBeDiscarded && !ConfirmingDiscard;

    private bool CanCancelDiscard() => !IsActing && ConfirmingDiscard;

    private bool CanConfirmDiscard() => IsOpen() && Status.CanBeDiscarded && ConfirmingDiscard;
}

[INotifyPropertyChanged]
internal sealed partial class ReviewExceptionViewModel : IReviewExceptionViewModel
{
    public ReviewExceptionViewModel(IReviewException exception)
    {
        Source = exception;
        (Title, Fact, Detail, Output) = ReviewPhrases.Exception(exception);
        Tone = ReviewPhrases.Tone(exception);
    }

    public IReviewException Source { get; }

    public string Title { get; }

    public string Fact { get; }

    public string Detail { get; }

    public string Output { get; }

    public ExceptionTone Tone { get; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }
}
