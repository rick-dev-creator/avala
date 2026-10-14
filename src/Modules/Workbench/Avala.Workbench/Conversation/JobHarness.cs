using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Accounts;
using Avala.Workbench.Board;
using Avala.Workbench.Harnesses;
using Avala.Workbench.ModelChoices;
using Avala.Workbench.NewJob;
using Avala.Workbench.Presenting;
using Avala.Workbench.Steering;

namespace Avala.Workbench.Conversation;

internal sealed record ColdConversation(TimeSpan Idle, long Tokens, Option<string> Compact);

internal sealed class JobHarness
{
    public const long ColdContext = 100_000;

    private readonly JobSwitching switching;
    private readonly NewJobReadings readings;
    private readonly ModelPickerViewModel models;
    private IReadOnlyList<ConnectionName> usable = [];
    private CapabilitySet capabilities = CapabilitySet.None;
    private Option<BoardJob> shown;
    private Option<ConnectionName> loaded;
    private string marked = string.Empty;
    private string guidance = string.Empty;
    private string error = string.Empty;
    private bool offering;
    private int loads;

    public JobHarness(JobId job, JobSwitching switching, NewJobReadings readings, ModelPickerViewModel models)
    {
        Job = job;
        this.switching = switching;
        this.readings = readings;
        this.models = models;
        Chip = new HarnessChoiceViewModel(JobHarnessPhrases.Heading, opensUpward: true, models);
        models.PropertyChanged += (_, changed) =>
        {
            if (!offering && models.CanChoose && changed.PropertyName is nameof(ModelPickerViewModel.Model) or nameof(ModelPickerViewModel.Effort))
            {
                Choosing = ChooseAsync(models.Chosen);
            }
        };
        Chip.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(HarnessChoiceViewModel.IsOpen) && Chip.IsOpen)
            {
                Loading = LoadAsync();
            }
        };
    }

    public event EventHandler? Loaded;

    public JobId Job { get; }

    public HarnessChoiceViewModel Chip { get; }

    public Task Loading { get; private set; } = Task.CompletedTask;

    public Task Choosing { get; private set; } = Task.CompletedTask;

    public Task Moving { get; private set; } = Task.CompletedTask;

    public Option<ColdConversation> Cold() =>
        shown.Bind(job => capabilities.Get<CachesPrompt>().Bind(cache =>
            job.Transcript.QuietSince.Bind(quiet => job.Transcript.Context.Bind(context =>
                readings.Now - quiet >= cache.Lifetime && context.Used >= ColdContext && !context.Compacted
                    ? Option<ColdConversation>.Some(new ColdConversation(readings.Now - quiet, context.Used, capabilities.Get<CompactsOnRequest>().Map(compact => compact.Message)))
                    : Option<ColdConversation>.None))));

    public void Track(BoardJob job)
    {
        shown = job;
        ShowLabel(job);

        if (job.Summary.Connection != loaded)
        {
            loaded = job.Summary.Connection;
            Loading = LoadAsync();
            return;
        }

        Mark(job);
    }

    private async Task LoadAsync()
    {
        var ticket = ++loads;
        var catalog = await switching.CatalogAsync(CancellationToken.None);
        var found = await loaded.Match(
            connection => switching.CapabilitiesAsync(connection, CancellationToken.None),
            () => Task.FromResult(CapabilitySet.None));

        if (ticket != loads)
        {
            return;
        }

        usable = catalog.Error.IsNone ? [.. catalog.Connections.Where(connection => connection.Problem.IsNone).Select(connection => connection.Name)] : [];
        capabilities = found;
        marked = string.Empty;
        shown.Match<Action>(job => () => Mark(job), () => () => { })();
        Loaded?.Invoke(this, EventArgs.Empty);
    }

    private void ShowLabel(BoardJob job)
    {
        var connection = job.Summary.Connection;
        var (model, effort) = Running(job);

        Chip.IsShown = connection.IsSome;
        Chip.Show(new HarnessChip(
            string.Join(" · ", new[] { connection.Map(name => name.Value), model, effort }.Where(part => part.IsSome).Select(part => part.Match(text => text, () => string.Empty))),
            connection.Match(name => AccountPhrases.Monogram(name.Value), () => string.Empty),
            connection.Match(name => JobHarnessPhrases.Runs(name, Reading(name)), () => string.Empty),
            false));
    }

    private void Mark(BoardJob job)
    {
        var connection = job.Summary.Connection;
        var status = job.Status;
        var others = status.IsOver || status == JobStatus.AwaitingReview ? [] : usable.Where(name => connection != Option<ConnectionName>.Some(name)).ToList();
        var key = $"{connection}|{string.Join(",", others)}|{status}|{job.Summary.Model}";

        if (key == marked)
        {
            return;
        }

        marked = key;
        var moves = status == JobStatus.NeedsHelp;
        Chip.Offer([
            .. connection.Match<HarnessOptionViewModel[]>(name => [new HarnessOptionViewModel(name.Value, name, Choose)], () => []),
            .. others.Select(name => new HarnessOptionViewModel(JobHarnessPhrases.ContinueOn(name), name, Choose)),
        ]);

        foreach (var option in Chip.Offered)
        {
            var chosen = option.Connection == connection;
            option.Show(option.Connection.Match(Reading, () => string.Empty), false, chosen, chosen || moves);
        }

        guidance = others.Count == 0 ? string.Empty : JobHarnessPhrases.Moving(moves);
        Offer(job);
        ShowNote();
    }

    private void Offer(BoardJob job)
    {
        var status = job.Status;
        var resumable = capabilities.Has<Resumable>();
        var idle = status is JobStatus.NeedsHelp or JobStatus.AwaitingReview;
        var connection = job.Summary.Connection.Match(name => name.Value, () => string.Empty);
        offering = true;
        capabilities.Get<OffersModels>().Match<Action>(
            offered => resumable && idle
                ? () => models.Offer(offered, ModelPhrases.Job(offered, ModelChoice.Default), job.Summary.Model, JobHarnessPhrases.NextMessage)
                : () => models.Follow(Current(job, offered), offered.Efforts.Count > 0, JobHarnessPhrases.Fixed(status, resumable, connection)),
            () => () => models.Hide(string.Empty))();
        offering = false;
    }

    private string Reading(ConnectionName connection) => readings.Reading(Option<Result<ConnectionPreview, JobRejection>>.None, connection);

    private static (Option<string> Model, Option<string> Effort) Running(BoardJob job)
    {
        var ran = job.Transcript.Ran;

        return (
            job.Summary.Model.Model.IsSome ? job.Summary.Model.Model : ran.Map(reported => reported.Model),
            job.Summary.Model.Effort.IsSome ? job.Summary.Model.Effort : ran.Bind(reported => reported.Effort));
    }

    private static ModelDefaults Current(BoardJob job, OffersModels offered)
    {
        var (model, effort) = Running(job);
        var defaults = ModelPhrases.Job(offered, ModelChoice.Default);

        return new ModelDefaults(model.Match(name => name, () => defaults.Model), effort.Match(name => name, () => defaults.Effort));
    }

    private void Choose(Option<ConnectionName> connection)
    {
        if (connection == loaded || connection.IsNone)
        {
            Chip.IsOpen = false;
            return;
        }

        Moving = connection.Match(MoveAsync, () => Task.CompletedTask);
    }

    private async Task MoveAsync(ConnectionName to)
    {
        var moved = await switching.HandOffAsync(Job, to, CancellationToken.None);
        error = moved.Match(_ => string.Empty, JobHarnessPhrases.Refused);
        ShowNote();
        Chip.IsOpen = !moved.IsSuccess && Chip.IsOpen;
    }

    private async Task ChooseAsync(ModelChoice model)
    {
        var chosen = await switching.ChooseModelAsync(Job, model, CancellationToken.None);
        error = chosen.Match(_ => string.Empty, JobHarnessPhrases.Refused);

        if (!chosen.IsSuccess)
        {
            marked = string.Empty;
            shown.Match<Action>(job => () => Mark(job), () => () => { })();
        }

        ShowNote();
    }

    private void ShowNote() => Chip.Note = error.Length > 0 ? error : guidance;
}

internal static class JobHarnessPhrases
{
    public const string Heading = "Connection";

    public const string NextMessage = "Applies from your next message: the conversation resumes with it.";

    public static string ContinueOn(ConnectionName connection) => $"Continue on {connection.Value}";

    public static string Runs(ConnectionName connection, string reading) => $"Runs on {connection.Value} · {reading}";

    public static string Moving(bool held) =>
        held
            ? "Continuing on another connection starts a new conversation there, with a brief of the work so far."
            : "Stop the agent to continue on another connection.";

    public static string Fixed(JobStatus status, bool resumable, string connection) =>
        status.IsOver ? string.Empty
        : !resumable ? $"{connection} cannot resume a conversation, so the job keeps its model."
        : "Change it once the agent stops.";

    public static string Refused(JobRejection rejection) => rejection switch
    {
        JobRejection.NotHeld => "Stop the agent first.",
        JobRejection.NotResumable => "The conversation cannot resume yet, so the job keeps its model.",
        JobRejection.UnofferedModel => "The connection does not offer that model.",
        JobRejection.UnofferedEffort => "The connection does not offer that effort.",
        JobRejection.SameConnection => "The job already runs there.",
        _ => ConversationPhrases.Rejection(rejection),
    };
}
