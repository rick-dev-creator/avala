using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Canvas.Contracts;
using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Timeline;

internal sealed record Transcript
{
    public static Transcript Empty { get; } = new();

    public ImmutableList<ITimelineEntry> Entries { get; private init; } = [];

    private ImmutableDictionary<string, int> Positions { get; init; } = ImmutableDictionary<string, int>.Empty;

    private ImmutableDictionary<TurnId, TurnTally> Turns { get; init; } = ImmutableDictionary<TurnId, TurnTally>.Empty;

    private ImmutableDictionary<int, HandoffRecord> Handoffs { get; init; } = ImmutableDictionary<int, HandoffRecord>.Empty;

    public IEnumerable<ITimelineEntry> Awaiting =>
        Entries.Where(entry => entry is PermissionEntry { AwaitsHuman: true } or FormEntry { AwaitsHuman: true });

    public Option<ModelReported> Ran { get; private init; }

    public Option<PlanEntry> Plan => Entries.OfType<PlanEntry>().LastOrDefault().ToOption();

    public Option<ITimelineEntry> Find(string key) =>
        Positions.TryGetValue(key, out var position) ? Option<ITimelineEntry>.Some(Entries[position]) : Option<ITimelineEntry>.None;

    public Transcript WithPrompts(string instruction, IReadOnlyList<AttemptRecord> attempts) =>
        attempts.Count == 0
            ? Positions.ContainsKey(EntryKeys.Attempt(1)) ? this : Add(new PromptEntry(EntryKeys.Attempt(1), 1, AttemptOrigin.Initial, instruction, Option<AttemptOutcome>.None))
            : attempts.Aggregate(this, (transcript, attempt) => transcript.Put(transcript.Prompt(instruction, attempt)));

    public Transcript WithPrompt(string instruction, AttemptRecord attempt) => Put(Prompt(instruction, attempt));

    public Transcript WithHandoff(HandoffRecord handoff)
    {
        var noted = this with { Handoffs = Handoffs.SetItem(handoff.Attempt, handoff) };

        return noted.Change(EntryKeys.Attempt(handoff.Attempt), entry => entry is PromptEntry prompt ? prompt with { Handoff = handoff } : entry);
    }

    public Transcript WithRestart(bool kept) =>
        Entries.IsEmpty || Positions.ContainsKey(EntryKeys.Restart) ? this : Add(new RestartEntry(EntryKeys.Restart, kept));

    public Transcript WithEarlierRestart(int number) => Add(new RestartEntry(EntryKeys.EarlierRestart(number), Kept: true));

    public Transcript Stopped(DateTimeOffset at) =>
        Entries.Aggregate(this, (transcript, entry) => entry switch
        {
            PermissionEntry { Closed: false } permission => transcript.Put(permission with { Closed = true }),
            FormEntry { Closed: false } form => transcript.Put(form with { Closed = true }),
            MessageEntry { Outcome.IsNone: true } message => transcript.Put(message with { Outcome = ItemOutcome.Abandoned }),
            ReasoningEntry { Outcome.IsNone: true } reasoning => transcript.Put(reasoning with { Outcome = ItemOutcome.Abandoned, Duration = at - reasoning.Started }),
            ToolEntry { Outcome.IsNone: true } tool => transcript.Put(tool with { Outcome = ItemOutcome.Abandoned }),
            CanvasEntry { Status: CanvasStatus.Streaming } canvas => transcript.Put(canvas with { Status = CanvasStatus.Abandoned }),
            _ => transcript,
        });

    public Transcript Apply(IAgentEvent activity, DateTimeOffset now) => activity switch
    {
        TurnStarted started => this with { Turns = Turns.SetItem(started.Turn, new TurnTally(now, default, [])) },
        ItemStarted started => Add(Opened(started, now)),
        CanvasStarted started => Add(new CanvasEntry(EntryKeys.Item(started.Turn, started.Item), started.Title, started.MediaType, string.Empty, CanvasStatus.Streaming)),
        ItemProgressed progressed => Change(EntryKeys.Item(progressed.Turn, progressed.Item), entry => Appended(entry, progressed.Text)),
        ItemCompleted completed => Change(EntryKeys.Item(completed.Turn, completed.Item), entry => Completed(entry, completed.Outcome, now)),
        MessageQueued queued => Add(new InterjectionEntry(EntryKeys.Interjection(queued.Turn, Entries.Count), queued.Text)),
        PlanUpdated plan => Put(new PlanEntry(EntryKeys.Plan(plan.Turn), plan.Steps)),
        UsageReported usage => Tally(usage.Turn, tally => tally.Add(usage.Tokens, usage.Cost)),
        ModelReported reported => this with { Ran = reported },
        TurnCompleted completed => Ended(completed, now),
        _ => Exchange(activity),
    };

    public Transcript Apply(CanvasSnapshot snapshot) =>
        Change(EntryKeys.Item(snapshot.Canvas.Turn, snapshot.Canvas.Item), entry => entry is CanvasEntry canvas
            ? canvas with { Title = snapshot.Title, MediaType = snapshot.MediaType, Content = snapshot.Content, Status = snapshot.Status, IsOffered = snapshot.IsOffered }
            : entry);

    public Transcript Apply(PolicyDecision decision) =>
        Change(EntryKeys.Permission(decision.Turn, decision.Item), entry => entry is PermissionEntry permission ? permission with { Decision = decision } : entry);

    public Transcript Apply(FormDecision decision) =>
        Change(EntryKeys.Item(decision.Turn, decision.Item), entry => entry is FormEntry form ? form with { Decision = decision } : entry);

    private Transcript Exchange(IAgentEvent activity) => activity switch
    {
        PermissionRequested requested => Add(new PermissionEntry(EntryKeys.Permission(requested.Turn, requested.Item), requested.Session, requested.Turn, requested.Item, requested.Kind, requested.Title, requested.Target)),
        PermissionResolved resolved => Change(EntryKeys.Permission(resolved.Turn, resolved.Item), entry => entry is PermissionEntry permission ? permission with { Resolution = resolved.Answer } : entry),
        FormRequested requested => Add(new FormEntry(EntryKeys.Item(requested.Turn, requested.Item), requested.Session, requested.Turn, requested.Item, requested.Form)),
        FormAnswered answered => Change(EntryKeys.Item(answered.Turn, answered.Item), entry => entry is FormEntry form ? form with { Answer = answered.Answer } : entry),
        RequestWithdrawn withdrawn => Withdrawn(withdrawn),
        ToolCalled called => Add(new ToolEntry(EntryKeys.Item(called.Turn, called.Item), ItemKind.Other, called.Tool, string.Empty, Option<ItemOutcome>.None) { Input = called.Input }),
        ToolReturned returned => Change(EntryKeys.Item(returned.Turn, returned.Item), entry => entry is ToolEntry tool ? tool with { Output = returned.Result.Content } : entry),
        _ => this,
    };

    private Transcript Withdrawn(RequestWithdrawn withdrawn) =>
        Change(EntryKeys.Permission(withdrawn.Turn, withdrawn.Item), entry => entry is PermissionEntry permission ? permission with { Withdrawn = true } : entry)
            .Change(EntryKeys.Item(withdrawn.Turn, withdrawn.Item), entry => entry is FormEntry form ? form with { Withdrawn = true } : entry);

    private PromptEntry Prompt(string instruction, AttemptRecord attempt) =>
        new(
            EntryKeys.Attempt(attempt.Number),
            attempt.Number,
            attempt.Origin,
            attempt.Origin == AttemptOrigin.Initial ? instruction : attempt.Guidance,
            attempt.Outcome)
        {
            Handoff = Handoffs.TryGetValue(attempt.Number, out var handoff) ? handoff : Option<HandoffRecord>.None,
        };

    private static ITimelineEntry Opened(ItemStarted started, DateTimeOffset now)
    {
        var key = EntryKeys.Item(started.Turn, started.Item);

        return started.Kind switch
        {
            ItemKind.Message => new MessageEntry(key, string.Empty, Option<ItemOutcome>.None),
            ItemKind.Reasoning => new ReasoningEntry(key, string.Empty, now, Option<TimeSpan>.None, Option<ItemOutcome>.None),
            _ => new ToolEntry(key, started.Kind, started.Title, string.Empty, Option<ItemOutcome>.None) { Input = started.Input },
        };
    }

    private static ITimelineEntry Appended(ITimelineEntry entry, string text) => entry switch
    {
        MessageEntry message => message with { Text = message.Text + text },
        ReasoningEntry reasoning => reasoning with { Text = reasoning.Text + text },
        ToolEntry tool => tool with { Output = tool.Output + text },
        _ => entry,
    };

    private static ITimelineEntry Completed(ITimelineEntry entry, ItemOutcome outcome, DateTimeOffset now) => entry switch
    {
        MessageEntry message => message with { Outcome = outcome },
        ReasoningEntry reasoning => reasoning with { Outcome = outcome, Duration = now - reasoning.Started },
        ToolEntry tool => tool with { Outcome = outcome },
        FormEntry form => form with { Outcome = outcome },
        _ => entry,
    };

    private Transcript Ended(TurnCompleted completed, DateTimeOffset now)
    {
        var tally = Turns.GetValueOrDefault(completed.Turn, new TurnTally(now, default, []));
        var closed = Entries.Aggregate(this, (transcript, entry) => entry switch
        {
            PermissionEntry permission when permission.Turn == completed.Turn => transcript.Put(permission with { Closed = true }),
            FormEntry form when form.Turn == completed.Turn => transcript.Put(form with { Closed = true }),
            _ => transcript,
        });

        return closed.Add(new TurnEndEntry(EntryKeys.TurnEnd(completed.Turn), completed.Outcome, now - tally.Started, tally.Tokens, tally.Costs));
    }

    private Transcript Tally(TurnId turn, Func<TurnTally, TurnTally> change) =>
        Turns.TryGetValue(turn, out var tally) ? this with { Turns = Turns.SetItem(turn, change(tally)) } : this;

    private Transcript Add(ITimelineEntry entry) =>
        Positions.ContainsKey(entry.Key)
            ? this
            : this with { Entries = Entries.Add(entry), Positions = Positions.Add(entry.Key, Entries.Count) };

    private Transcript Put(ITimelineEntry entry) =>
        Positions.TryGetValue(entry.Key, out var position)
            ? this with { Entries = Entries.SetItem(position, entry) }
            : Add(entry);

    private Transcript Change(string key, Func<ITimelineEntry, ITimelineEntry> change) =>
        Positions.TryGetValue(key, out var position)
            ? this with { Entries = Entries.SetItem(position, change(Entries[position])) }
            : this;

    private sealed record TurnTally(DateTimeOffset Started, TokenUsage Tokens, ImmutableList<Cost> Costs)
    {
        public TurnTally Add(TokenUsage tokens, Option<Cost> cost) =>
            this with
            {
                Tokens = new TokenUsage(
                    Tokens.Input + tokens.Input,
                    Tokens.Output + tokens.Output,
                    Tokens.CacheRead + tokens.CacheRead,
                    Tokens.CacheWrite + tokens.CacheWrite,
                    Tokens.Reasoning + tokens.Reasoning),
                Costs = cost.Match(Merged, () => Costs),
            };

        private ImmutableList<Cost> Merged(Cost cost) =>
            Costs.FindIndex(known => known.Currency == cost.Currency) is var index and >= 0
                ? Costs.SetItem(index, Costs[index] with { Amount = Costs[index].Amount + cost.Amount })
                : Costs.Add(cost);
    }
}
