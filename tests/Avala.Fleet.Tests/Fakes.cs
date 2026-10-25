using System.Text.Json.Nodes;
using Avala.Fleet.Connecting;
using Avala.Fleet.Contracts;
using Avala.Fleet.Delivering;
using Avala.Fleet.Delivery;
using Avala.Fleet.Facts;
using Avala.Fleet.Recording;
using Avala.Fleet.Server;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Fleet.Tests;

internal sealed class StoredEntry(string scope, Fact fact)
{
    public string Scope { get; } = scope;

    public Fact Fact { get; } = fact;

    public int Attempts { get; set; }

    public DateTimeOffset DueAt { get; set; } = DateTimeOffset.MinValue;

    public Option<DateTimeOffset> FirstDeferredAt { get; set; }

    public string Kind => Fact.Body.Kind;
}

internal sealed class FakeFactStore : IFactStore
{
    public List<StoredEntry> Entries { get; } = [];

    public HashSet<(string Scope, Guid Job)> Announced { get; } = [];

    public List<(string Scope, RejectionNote Note)> Rejections { get; } = [];

    public Dictionary<string, int> Dropped { get; } = [];

    public Dictionary<string, DateTimeOffset> Delivered { get; } = [];

    public Task<bool> IsAnnouncedAsync(string scope, Guid job, CancellationToken cancellationToken) => Task.FromResult(Announced.Contains((scope, job)));

    public Task AnnounceAsync(string scope, Guid job, bool keyless, CancellationToken cancellationToken)
    {
        Announced.Add((scope, job));

        return Task.CompletedTask;
    }

    public Task AppendAsync(string scope, Fact fact, CancellationToken cancellationToken)
    {
        Entries.Add(new StoredEntry(scope, fact));

        return Task.CompletedTask;
    }

    public Task<DateTimeOffset> LatestStampAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Entries.Count == 0 ? DateTimeOffset.MinValue : Entries.Max(entry => entry.Fact.OccurredAt));

    public int FailsDue { get; set; }

    public Task<IReadOnlyList<StoredFact>> DueAsync(string scope, DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        FailsDue-- > 0
            ? Task.FromException<IReadOnlyList<StoredFact>>(new IOException("the outbox could not be read"))
            : Task.FromResult<IReadOnlyList<StoredFact>>(
            [.. Entries.Where(entry => entry.Scope == scope && entry.DueAt <= now).Take(limit).Select(entry => new StoredFact(entry.Fact.Id, entry.Kind, WireJson.Write(entry.Fact)))]);

    public Task<Option<DateTimeOffset>> NextDueAsync(string scope, CancellationToken cancellationToken) =>
        Task.FromResult(Entries.Where(entry => entry.Scope == scope).Select(entry => Option<DateTimeOffset>.Some(entry.DueAt)).DefaultIfEmpty(Option<DateTimeOffset>.None).Min(due => due.Match(found => found, () => DateTimeOffset.MaxValue)) is var next && next != DateTimeOffset.MaxValue ? next : Option<DateTimeOffset>.None);

    public Task SettleAsync(string scope, ReceiptPlan plan, DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var rejected in plan.Rejected.Where(fact => Entries.Exists(entry => entry.Fact.Id == fact.Id)))
        {
            Rejections.Add((scope, new RejectionNote(Entries.Single(entry => entry.Fact.Id == rejected.Id).Kind, rejected.Reason, now)));
        }

        Entries.RemoveAll(entry => plan.Delete.Contains(entry.Fact.Id));

        foreach (var deferred in plan.Deferred.Where(fact => Entries.Exists(entry => entry.Fact.Id == fact.Id)))
        {
            var entry = Entries.Single(found => found.Fact.Id == deferred.Id);
            entry.Attempts++;
            entry.DueAt = now + Backoff.After(entry.Attempts);
            entry.FirstDeferredAt = entry.FirstDeferredAt.IsSome ? entry.FirstDeferredAt : now;
        }

        Delivered[scope] = now;

        return Task.CompletedTask;
    }

    public Task<int> ExpireAsync(string scope, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var dropped = Entries.RemoveAll(entry => entry.Scope == scope && entry.FirstDeferredAt.Match(first => DeferredExpiry.Expired(first, now), () => false));
        Dropped[scope] = Dropped.GetValueOrDefault(scope) + dropped;

        return Task.FromResult(dropped);
    }

    public Task<OutboxSummary> SummaryAsync(string scope, CancellationToken cancellationToken) =>
        Task.FromResult(new OutboxSummary(
            Entries.Count(entry => entry.Scope == scope),
            Delivered.TryGetValue(scope, out var at) ? at : Option<DateTimeOffset>.None,
            [.. Rejections.Where(rejection => rejection.Scope == scope).Select(rejection => rejection.Note)],
            Dropped.GetValueOrDefault(scope)));

    public Task KeepOnlyAsync(string scope, CancellationToken cancellationToken)
    {
        Entries.RemoveAll(entry => entry.Scope != scope);
        Announced.RemoveWhere(announced => announced.Scope != scope);

        return Task.CompletedTask;
    }

    public Task ForgetAsync(CancellationToken cancellationToken)
    {
        Entries.Clear();
        Announced.Clear();
        Rejections.Clear();
        Delivered.Clear();
        Dropped.Clear();

        return Task.CompletedTask;
    }
}

internal sealed class FakeJobFacts : IJobFacts
{
    public HashSet<JobId> Scratch { get; } = [];

    public Dictionary<JobId, JobAnnouncement> Announcements { get; } = [];

    public HashSet<JobId> UnknownOnce { get; } = [];

    public ValueTask<Option<bool>> IsScratchAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(UnknownOnce.Remove(job) ? Option<bool>.None : Scratch.Contains(job));

    public ValueTask<Option<JobAnnouncement>> AnnouncementAsync(JobId job, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Announcements.TryGetValue(job, out var found) ? found : Option<JobAnnouncement>.None);
}

internal sealed class FakeFleetFile(Option<FleetSettings> settings = default) : IFleetFile
{
    public Option<FleetSettings> Settings { get; private set; } = settings;

    public bool Writable { get; set; } = true;

    public Task<Option<FleetSettings>> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);

    public Task<bool> WriteAsync(FleetSettings settings, CancellationToken cancellationToken)
    {
        if (Writable)
        {
            Settings = settings;
        }

        return Task.FromResult(Writable);
    }

    public Task<bool> DeleteAsync(CancellationToken cancellationToken)
    {
        Settings = Option<FleetSettings>.None;

        return Task.FromResult(true);
    }
}

internal sealed class FakeKeychain : IKeychain
{
    public Dictionary<string, string> Keys { get; } = new(StringComparer.Ordinal);

    public bool Available { get; set; } = true;

    public int Reads { get; private set; }

    public bool FailsReading { get; set; }

    public ValueTask<bool> AvailableAsync(CancellationToken cancellationToken) => ValueTask.FromResult(Available);

    public ValueTask<Result<string, KeychainFailure>> StoreAsync(string account, string secret, CancellationToken cancellationToken)
    {
        if (!Available)
        {
            return ValueTask.FromResult<Result<string, KeychainFailure>>(KeychainFailure.Unavailable);
        }

        Keys[account] = secret;

        return ValueTask.FromResult<Result<string, KeychainFailure>>(account);
    }

    public ValueTask<Result<Option<string>, KeychainFailure>> ReadAsync(string account, CancellationToken cancellationToken)
    {
        Reads++;

        return ValueTask.FromResult<Result<Option<string>, KeychainFailure>>(
            FailsReading ? KeychainFailure.Denied : Keys.TryGetValue(account, out var key) ? key : Option<string>.None);
    }

    public bool FailsRemoving { get; set; }

    public ValueTask<Result<string, KeychainFailure>> RemoveAsync(string account, CancellationToken cancellationToken)
    {
        if (FailsRemoving)
        {
            return ValueTask.FromResult<Result<string, KeychainFailure>>(KeychainFailure.Denied);
        }

        Keys.Remove(account);

        return ValueTask.FromResult<Result<string, KeychainFailure>>(account);
    }
}

internal sealed class FakeFleetServer : IFleetServer
{
    public List<(string Server, string Key, IReadOnlyList<JsonNode> Events)> Batches { get; } = [];

    public Queue<Func<IReadOnlyList<Guid>, SendOutcome>> Answers { get; } = [];

    public ServerAnswer Identifies { get; set; } = ServerAnswer.Answered;

    public Identity Identity { get; set; } = new(Guid.Parse("0199f6a0-0000-7000-8000-00000000000a"), "desktop");

    public FleetView Repositories { get; set; } = FleetView.Of(FleetReadState.Read);

    public List<string> Asked { get; } = [];

    public static SendOutcome AcceptAll(IReadOnlyList<Guid> ids) => new(ServerAnswer.Answered, new Receipt(ids, [], [], []));

    public Task<SendOutcome> SendAsync(string server, string key, IReadOnlyList<string> bodies, CancellationToken cancellationToken)
    {
        var events = bodies.Select(body => JsonNode.Parse(body)!).ToList();
        Batches.Add((server, key, events));
        var ids = events.Select(found => Guid.Parse(found["id"]!.GetValue<string>())).ToList();

        return Task.FromResult(Answers.Count > 0 ? Answers.Dequeue()(ids) : AcceptAll(ids));
    }

    public Task<IdentifyOutcome> IdentifyAsync(string server, string key, CancellationToken cancellationToken) =>
        Task.FromResult(Identifies == ServerAnswer.Answered ? new IdentifyOutcome(ServerAnswer.Answered, Identity) : new IdentifyOutcome(Identifies, Option<Identity>.None));

    public Task<FleetView> RepositoriesAsync(string server, string key, CancellationToken cancellationToken)
    {
        Asked.Add($"repositories {server} {key}");

        return Task.FromResult(Repositories);
    }

    public Task<FleetJobsView> JobsAsync(string server, string key, string repository, CancellationToken cancellationToken)
    {
        Asked.Add($"jobs {repository}");

        return Task.FromResult(FleetJobsView.Of(FleetReadState.Read));
    }
}

internal static class Fleets
{
    public const string Server = "https://fleet.example";

    public const string Key = "avk_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    public static readonly Guid Instance = Guid.Parse("0199f6a0-0000-7000-8000-00000000000a");

    public static FleetSettings Settings(bool enabled = true, bool refused = false) => new(Server, Instance, "desktop", enabled, refused);

    public static string Scope => Settings().Scope.Id;

    public static async Task<FleetSession> SessionAsync(FakeKeychain keychain, Option<FleetSettings> settings)
    {
        var session = new FleetSession(new FakeFleetFile(settings), keychain);
        await session.RunAsync(TestContext.Current.CancellationToken);

        return session;
    }

    public static Task<FleetSession> ConnectedAsync(FakeKeychain keychain, bool enabled = true)
    {
        keychain.Keys[Server] = Key;

        return SessionAsync(keychain, Settings(enabled));
    }
}
