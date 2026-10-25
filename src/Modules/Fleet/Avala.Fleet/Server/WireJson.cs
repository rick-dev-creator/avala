using System.Globalization;
using System.Text;
using System.Text.Json;
using Avala.Fleet.Contracts;
using Avala.Fleet.Delivering;
using Avala.Fleet.Delivery;
using Avala.Fleet.Facts;
using Avala.Sdk;

namespace Avala.Fleet.Server;

internal static class WireJson
{
    private static readonly JsonSerializerOptions Reading = new(JsonSerializerDefaults.Web) { RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true };

    public static string Write(Fact fact)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", fact.Body.Kind);
            writer.WriteString("id", fact.Id.ToString("D", CultureInfo.InvariantCulture));
            writer.WriteString("occurredAt", Instant(fact.OccurredAt));
            Fields(writer, fact.Body);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    public static string Batch(IReadOnlyList<string> bodies) => $"{{\"events\":[{string.Join(',', bodies)}]}}";

    public static Option<Receipt> ReadReceipt(string text) =>
        Read<ReceiptBody>(text).Map(body => new Receipt(
            body.Accepted,
            body.Duplicates,
            [.. body.Deferred.Select(entry => new ReceiptEntry(entry.Id, entry.Reason))],
            [.. body.Rejected.Select(entry => new ReceiptEntry(entry.Id, entry.Reason))]));

    public static Option<Identity> ReadIdentity(string text) =>
        Read<IdentityBody>(text).Map(body => new Identity(body.Id, body.Name));

    public static Option<FleetView> ReadRepositories(string text) =>
        Read<RepositoriesBody>(text).Map(body => new FleetView(
            FleetReadState.Read,
            [
                .. body.Repositories.Select(repository => new FleetRepository(
                    repository.Key,
                    repository.Jobs,
                    repository.Instances,
                    TokensOf(repository.Tokens),
                    CostsOf(repository.Costs),
                    repository.FirstActivityAt,
                    repository.LastActivityAt)),
            ],
            body.UnattributedJobs));

    public static Option<FleetJobsView> ReadJobs(string text) =>
        Read<JobsBody>(text).Map(body => new FleetJobsView(
            FleetReadState.Read,
            [
                .. body.Jobs.Select(job => new FleetJob(
                    job.Instance,
                    job.InstanceName,
                    job.Job,
                    Text(job.Status),
                    Text(job.Hold),
                    job.Attempts,
                    Text(job.LastOutcome),
                    TokensOf(job.Tokens),
                    CostsOf(job.Costs),
                    job.LastActivityAt)),
            ]));

    private static string Instant(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);

    private static void Fields(Utf8JsonWriter writer, IFactBody body)
    {
        switch (body)
        {
            case JobSubmittedFact submitted:
                writer.WriteString("job", submitted.Job.ToString("D", CultureInfo.InvariantCulture));
                writer.WriteString("repository", submitted.Repository);
                Optional(writer, "parent", submitted.Parent.Map(parent => parent.ToString("D", CultureInfo.InvariantCulture)));
                Optional(writer, "model", submitted.Model);
                Optional(writer, "effort", submitted.Effort);
                break;
            case JobModelChosenFact chosen:
                writer.WriteString("job", chosen.Job.ToString("D", CultureInfo.InvariantCulture));
                Optional(writer, "model", chosen.Model);
                Optional(writer, "effort", chosen.Effort);
                break;
            case JobProgressedFact progressed:
                writer.WriteString("job", progressed.Job.ToString("D", CultureInfo.InvariantCulture));
                writer.WriteString("status", progressed.Status);
                break;
            case JobHeldFact held:
                writer.WriteString("job", held.Job.ToString("D", CultureInfo.InvariantCulture));
                writer.WriteString("reason", held.Reason);
                break;
            case UsageReportedFact usage:
                Optional(writer, "job", usage.Job.Map(job => job.ToString("D", CultureInfo.InvariantCulture)));
                writer.WriteNumber("inputTokens", usage.Tokens.Input);
                writer.WriteNumber("outputTokens", usage.Tokens.Output);
                writer.WriteNumber("cacheReadTokens", usage.Tokens.CacheRead);
                writer.WriteNumber("cacheWriteTokens", usage.Tokens.CacheWrite);
                writer.WriteNumber("reasoningTokens", usage.Tokens.Reasoning);
                usage.Cost.Match(
                    cost =>
                    {
                        writer.WriteStartObject("cost");
                        writer.WriteNumber("amount", cost.Amount);
                        writer.WriteString("currency", cost.Currency);
                        writer.WriteString("basis", cost.Basis);
                        writer.WriteEndObject();

                        return true;
                    },
                    () => false);
                Optional(writer, "model", usage.Model);
                break;
            case AttemptVerifiedFact verified:
                writer.WriteString("job", verified.Job.ToString("D", CultureInfo.InvariantCulture));
                writer.WriteNumber("attempt", verified.Attempt);
                writer.WriteString("outcome", verified.Outcome);
                break;
        }
    }

    private static void Optional(Utf8JsonWriter writer, string name, Option<string> value) =>
        value.Match(
            present =>
            {
                writer.WriteString(name, present);

                return true;
            },
            () => false);

    private static string Text(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : string.Empty;

    private static FleetTokens TokensOf(TokensBody tokens) =>
        new(tokens.Input, tokens.Output, tokens.CacheRead, tokens.CacheWrite, tokens.Reasoning);

    private static IReadOnlyList<FleetCost> CostsOf(IReadOnlyList<CostBody> costs) =>
        [.. costs.Select(cost => new FleetCost(cost.Currency, cost.Basis, cost.Amount))];

    private static Option<T> Read<T>(string text)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text, Reading) is { } read ? read : Option<T>.None;
        }
        catch (JsonException)
        {
            return Option<T>.None;
        }
    }

    private sealed record ReceiptBody(IReadOnlyList<Guid> Accepted, IReadOnlyList<Guid> Duplicates, IReadOnlyList<EntryBody> Deferred, IReadOnlyList<EntryBody> Rejected);

    private sealed record EntryBody(string Id, string Reason);

    private sealed record IdentityBody(Guid Id, string Name);

    private sealed record TokensBody(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite, decimal Reasoning);

    private sealed record CostBody(string Currency, string Basis, decimal Amount);

    private sealed record RepositoryBody(
        string Key,
        int Jobs,
        int Instances,
        TokensBody Tokens,
        IReadOnlyList<CostBody> Costs,
        DateTimeOffset FirstActivityAt,
        DateTimeOffset LastActivityAt);

    private sealed record RepositoriesBody(IReadOnlyList<RepositoryBody> Repositories, int UnattributedJobs);

    private sealed record JobBody(
        Guid Instance,
        string InstanceName,
        Guid Job,
        JsonElement Status,
        JsonElement Hold,
        int Attempts,
        JsonElement LastOutcome,
        TokensBody Tokens,
        IReadOnlyList<CostBody> Costs,
        DateTimeOffset LastActivityAt);

    private sealed record JobsBody(IReadOnlyList<JobBody> Jobs);
}
