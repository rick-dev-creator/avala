using System.Collections.Frozen;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Avala.Server.Activity.Contracts;
using Avala.Server.Activity.Contracts.Ingestion;
using Avala.Server.Activity.EndPoints.Ingestion.Wire;

namespace Avala.Server.Activity.EndPoints.Ingestion;

internal static partial class IngestEventsMapper
{
    private const string KindProperty = "kind";

    private const string IdProperty = "id";

    private static readonly JsonDocumentOptions Document = new() { AllowDuplicateProperties = false };

    private static readonly FrozenDictionary<string, Func<JsonElement, IIncomingEvent>> Kinds =
        new Dictionary<string, Func<JsonElement, IIncomingEvent>>(StringComparer.Ordinal)
        {
            ["jobSubmitted"] = element => ToEvent(Read<JobSubmittedBody>(element)),
            ["jobModelChosen"] = element => ToEvent(Read<JobModelChosenBody>(element)),
            ["jobProgressed"] = element => ToEvent(Read<JobProgressedBody>(element)),
            ["jobHeld"] = element => ToEvent(Read<JobHeldBody>(element)),
            ["usageReported"] = element => ToEvent(Read<UsageReportedBody>(element)),
            ["attemptVerified"] = element => ToEvent(Read<AttemptVerifiedBody>(element)),
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static JsonSerializerOptions Json { get; } = Options();

    public static MappedBatch ToCommand(ClaimsPrincipal caller, IngestEventsRequest request)
    {
        List<IIncomingEvent> events = [];
        List<IngestEventsEntry> rejected = [];
        List<IngestEventsEntry> deferred = [];
        HashSet<Guid> seen = [];
        var elements = request.Events ?? [];

        for (var index = 0; index < elements.Count; index++)
        {
            var reading = ReadElement(elements[index], index);

            if (reading.Id is { } id && !seen.Add(id))
            {
                continue;
            }

            if (reading.Event is { } incoming)
            {
                events.Add(incoming);
            }
            else
            {
                (reading.Deferred ? deferred : rejected).Add(new IngestEventsEntry(reading.Key, reading.Reason ?? string.Empty));
            }
        }

        return new MappedBatch(new IngestEvents(InstanceOf(caller), caller.Identity?.Name ?? string.Empty, events), rejected, deferred);
    }

    public static IngestEventsResponse ToResponse(IngestionReceipt receipt, MappedBatch mapped) =>
        new(
            receipt.Accepted,
            receipt.Duplicates,
            mapped.Deferred,
            [.. mapped.Rejected, .. receipt.Rejected.Select(refused => new IngestEventsEntry(refused.Id.ToString(), refused.Reason))]);

    private static Reading ReadElement(JsonElement element, int index)
    {
        var position = $"#{index}";

        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Reading(position, null, null, "an event must be an object", Deferred: false);
        }

        var id = Guid.TryParse(TextOf(element, IdProperty), out var parsed) ? parsed : (Guid?)null;
        var key = id?.ToString() ?? position;

        if (RepeatsAProperty(element))
        {
            return new Reading(key, id, null, "the event repeats a property", Deferred: false);
        }

        if (TextOf(element, KindProperty) is not { } kind)
        {
            return new Reading(key, id, null, "the event has no kind", Deferred: false);
        }

        if (!Kinds.TryGetValue(kind, out var read))
        {
            return new Reading(key, id, null, $"unknown kind '{kind}'", Deferred: true);
        }

        if (HoldsBrokenText(element))
        {
            return new Reading(key, id, null, "the event holds text that is not valid Unicode", Deferred: false);
        }

        try
        {
            return new Reading(key, id, read(element), null, Deferred: false);
        }
        catch (JsonException exception)
        {
            return new Reading(key, id, null, Explain(exception), Deferred: false);
        }
    }

    private static bool HoldsBrokenText(JsonElement element)
    {
        try
        {
            ReadAllText(element);

            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private static void ReadAllText(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    _ = property.Name;
                    ReadAllText(property.Value);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    ReadAllText(item);
                }

                break;
            case JsonValueKind.String:
                _ = element.GetString();
                break;
        }
    }

    private static string? TextOf(JsonElement element, string property)
    {
        try
        {
            return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool RepeatsAProperty(JsonElement element)
    {
        try
        {
            using var document = JsonDocument.Parse(element.GetRawText(), Document);

            return false;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static string Explain(JsonException exception) =>
        exception.Path is { Length: > 2 } path && path.StartsWith("$.", StringComparison.Ordinal) ? $"the field '{path[2..]}' is invalid"
        : MissingFields().Match(exception.Message) is { Success: true } missing
            ? missing.Groups["more"].Success
                ? $"the fields {missing.Groups["fields"].Value} are missing"
                : $"the field {missing.Groups["fields"].Value} is missing"
        : "the event is malformed";

    [GeneratedRegex(@"missing required properties including: (?<fields>'[^']+'(?<more>, '[^']+')*)",RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MissingFields();

    private static T Read<T>(JsonElement element) =>
        element.Deserialize<T>(Json) ?? throw new JsonException("The event is null.");

    private static JobSubmittedEvent ToEvent(JobSubmittedBody body) =>
        new(body.Id, body.OccurredAt, body.Job, body.Repository, body.Parent, body.Model, body.Effort);

    private static JobModelChosenEvent ToEvent(JobModelChosenBody body) =>
        new(body.Id, body.OccurredAt, body.Job, body.Model, body.Effort);

    private static JobProgressedEvent ToEvent(JobProgressedBody body) =>
        new(body.Id, body.OccurredAt, body.Job, body.Status);

    private static JobHeldEvent ToEvent(JobHeldBody body) =>
        new(body.Id, body.OccurredAt, body.Job, body.Reason);

    private static UsageReportedEvent ToEvent(UsageReportedBody body) =>
        new(
            body.Id,
            body.OccurredAt,
            body.Job,
            body.InputTokens,
            body.OutputTokens,
            body.CacheReadTokens,
            body.CacheWriteTokens,
            body.ReasoningTokens,
            body.Cost is { } cost ? new IncomingCost(cost.Amount, cost.Currency, cost.Basis) : null,
            body.Model);

    private static AttemptVerifiedEvent ToEvent(AttemptVerifiedBody body) =>
        new(body.Id, body.OccurredAt, body.Job, body.Attempt, body.Outcome);

    private static Guid InstanceOf(ClaimsPrincipal caller) =>
        Guid.Parse(caller.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty);

    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = false,
            AllowDuplicateProperties = false,
            NumberHandling = JsonNumberHandling.Strict,
            RespectRequiredConstructorParameters = true,
            RespectNullableAnnotations = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
        };
        options.Converters.Add(new StrictEnumConverter<JobStatus>());
        options.Converters.Add(new StrictEnumConverter<HoldReason>());
        options.Converters.Add(new StrictEnumConverter<VerificationOutcome>());
        options.Converters.Add(new StrictEnumConverter<CostBasis>());
        options.Converters.Add(new StrictInstantConverter());
        options.MakeReadOnly(populateMissingResolver: true);

        return options;
    }

    private sealed record Reading(string Key, Guid? Id, IIncomingEvent? Event, string? Reason, bool Deferred);
}
