using Avala.Agents.Contracts.Sessions;
using Avala.Forges.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Board;
using Avala.Workbench.Drafting;
using Avala.Workbench.Steering;
using Avala.Workbench.Harnesses;
using Avala.Workbench.Timeline;
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

    IMentionsViewModel Mentions { get; }

    IRelayCommand RecallEarlierCommand { get; }

    IRelayCommand RecallLaterCommand { get; }

    IAsyncRelayCommand<int> RestoreCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ComposerViewModel : IComposerViewModel
{
    private readonly JobSteering steering;
    private readonly SendKeys keys;
    private readonly AttachmentTrayViewModel attachments;
    private readonly MentionsViewModel mentions;
    private readonly DraftBook drafts;
    private readonly IJobCheckpoints checkpoints;

    private readonly JobHarness harness;
    private List<string> said = [];
    private int recalled = -1;
    private bool remembering;

    public ComposerViewModel(JobId job, JobSteering steering, ComposerTools tools, JobHarness harness)
    {
        Job = job;
        this.harness = harness;
        this.steering = steering;
        keys = tools.Keys;
        attachments = tools.Attachments;
        mentions = tools.Mentions;
        drafts = tools.Drafts;
        checkpoints = tools.Checkpoints;
        mentions.Chosen += (_, chosen) => Edit(chosen.Draft);
        SendsWithEnter = keys.WithEnter;
        harness.Loaded += (_, _) =>
        {
            ShowCold();
            attachments.Accept(harness.Attachments);
        };
        Draft = string.Empty;
        Error = string.Empty;
        Queued = string.Empty;
        drafts.Of(job).Match(
            kept =>
            {
                Draft = kept.Text;
                attachments.Restore(kept.Attachments);
                mentions.Restore(kept.Mentions);

                return true;
            },
            () => false);
        attachments.Changed += (_, _) => Remember();
        remembering = true;
    }

    public JobId Job { get; }

    public IHarnessChoiceViewModel Harness => harness.Chip;

    public JobHarness Driver => harness;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsStop), nameof(ShowsSend))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(RecallEarlierCommand), nameof(RecallLaterCommand))]
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
    [NotifyPropertyChangedFor(nameof(AcceptsMessages), nameof(ShowsSend))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    public partial bool TakesMessages { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    public partial Option<PullRequestWatchState> Watch { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    public partial Option<HoldReason> Hold { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    public partial Option<JobFailure> Failure { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    public partial bool FindsChecks { get; private set; }

    [ObservableProperty]
    public partial string Error { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(WithdrawCommand))]
    public partial string Queued { get; private set; }

    public bool AcceptsMessages => TakesMessages || Status.IsWorking;

    public bool CanHalt => Status.CanBeInterrupted;

    public bool ShowsStop => CanHalt && string.IsNullOrWhiteSpace(Draft);

    public bool ShowsSend => AcceptsMessages && !ShowsStop;

    [ObservableProperty]
    public partial bool SendsWithEnter { get; private set; }

    [ObservableProperty]
    public partial int Edits { get; private set; }

    public string Placeholder => Closed.Match(note => note, () => FindsChecks ? ConversationPhrases.FindingChecks : ConversationPhrases.Placeholder(Status, TakesMessagesMidTurn, Watch, Hold, Failure));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Placeholder))]
    public partial Option<string> Closed { get; private set; }

    public string SendHint => ConversationPhrases.SendHint(Status, TakesMessagesMidTurn);

    public string QueuedCaption => ConversationPhrases.QueuedCaption(Status);

    public void Track(BoardJob shown)
    {
        Status = shown.Status;
        TakesMessages = shown.TakesMessages;
        Closed = ConversationPhrases.Closed(shown);
        TakesMessagesMidTurn = shown.TakesMessagesMidTurn;
        Watch = shown.PullRequest;
        Hold = shown.Hold;
        Failure = shown.Status == JobStatus.Failed ? shown.Summary.Failure : Option<JobFailure>.None;
        FindsChecks = shown.FindsChecks && shown.Status == JobStatus.Checking;
        SendsWithEnter = keys.WithEnter;
        ShowQueued();
        harness.Track(shown);
        ShowCold();
        Recollect(shown);
    }

    partial void OnDraftChanged(string value)
    {
        ShowCold();
        mentions.Look(value);
        recalled = Recalling ? recalled : -1;
        Remember();
    }

    private bool Recalling => recalled >= 0 && recalled < said.Count && Draft == said[^(recalled + 1)];

    private void Remember()
    {
        if (remembering)
        {
            drafts.Keep(Job, new Draft(Draft, attachments.Attached, mentions.Mentioned));
        }
    }

    private void Recollect(BoardJob shown)
    {
        var spoken = shown.Transcript.Entries.Select(entry => entry switch
            {
                PromptEntry { IsFromPerson: true } prompt => prompt.Text,
                InterjectionEntry { Author: MessageAuthor.Person } interjection => interjection.Text,
                _ => Option<string>.None,
            })
            .SelectMany(text => text.Match<string[]>(found => found.Trim().Length > 0 ? [found.Trim()] : [], () => []))
            .ToList();

        if (!spoken.SequenceEqual(said))
        {
            said = spoken;
            RecallEarlierCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task RestoreAsync(int attempt, CancellationToken cancellationToken)
    {
        var restored = await checkpoints.RestoreAsync(Job, attempt, cancellationToken);
        Error = restored.Match(_ => string.Empty, ConversationPhrases.Rejection);

        foreach (var done in restored.Match<CheckpointRestored[]>(found => [found], _ => []))
        {
            Edit(ConversationPhrases.Restored(done));
        }
    }

    [RelayCommand(CanExecute = nameof(CanRecallEarlier))]
    private void RecallEarlier()
    {
        recalled++;
        Edit(said[^(recalled + 1)]);
    }

    private bool CanRecallEarlier() => (Draft.Length == 0 || Recalling) && recalled + 1 < said.Count;

    [RelayCommand(CanExecute = nameof(Recalling))]
    private void RecallLater()
    {
        recalled--;
        Edit(recalled >= 0 ? said[^(recalled + 1)] : string.Empty);
    }

    private void ShowCold()
    {
        var cold = TakesMessages && !Status.IsWorking ? harness.Cold() : Option<ColdConversation>.None;
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
        var sent = await steering.SendAsync(Job, Status, new JobMessage(mentions.References(Draft.Trim())) { Attachments = attachments.Attached }, cancellationToken);
        Draft = sent.IsSuccess ? string.Empty : Draft;

        if (sent.IsSuccess)
        {
            attachments.Clear();
            mentions.Forget();
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

    public IMentionsViewModel Mentions => mentions;

    private void ShowQueued()
    {
        Queued = steering.Queued(Job).Match(message => message, () => string.Empty);

        var returned = drafts.TakeReturned(Job);

        if (returned.Count > 0)
        {
            var text = string.Join("\n\n", returned.Select(message => message.Text));
            Edit(string.IsNullOrWhiteSpace(Draft) ? text : $"{text}\n\n{Draft}");
            attachments.Restore([.. returned.SelectMany(message => message.Attachments)]);
            Error = ConversationPhrases.Undelivered(returned.Select(message => message.Why).LastOrDefault(why => why.IsSome));
        }
    }

    private void Report(Result<JobId, JobRejection> outcome) =>
        Error = outcome.Match(_ => string.Empty, ConversationPhrases.Rejection);
}
