using System.Text;
using System.Text.Json;
using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Triggers.Declarations;

namespace Avala.Triggers.TriggerFiles;

internal static class TriggerFileParser
{
    public const int MaximumBytes = 64 * 1024;

    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 5, AllowDuplicateProperties = false };

    private static readonly string[] Common =
        ["id", "enabled", "schedule", "webhook", "catchUp", "instruction", "target", "connection", "autonomy", "attempts", "concurrency"];

    private static readonly Dictionary<string, CatchUp> CatchUps = new(StringComparer.Ordinal) { ["none"] = CatchUp.None, ["once"] = CatchUp.Once };

    private static readonly Dictionary<string, TriggerTarget> Targets = new(StringComparer.Ordinal) { ["job"] = TriggerTarget.Job, ["loop"] = TriggerTarget.Loop };

    private static readonly Dictionary<string, Autonomy> Levels = new(StringComparer.Ordinal) { ["supervised"] = Autonomy.Supervised, ["autonomous"] = Autonomy.Autonomous };

    public static Result<TriggerFileContent, TriggerError> ParseMachine(string text) =>
        Read(text, root => Fields(root, ["repositories", "triggers"])
            .Bind(_ => Listed(root))
            .Bind(repositories => Triggers(root, entry => Trigger(entry, TriggerId.Machine, Option<string>.None))
                .Map(triggers => new TriggerFileContent(repositories, triggers))));

    public static Result<IReadOnlyList<TriggerDeclaration>, TriggerError> ParseRepository(string text, string repository) =>
        Read(text, root => Fields(root, ["triggers"]).Bind(_ => Triggers(root, entry => Trigger(entry, repository, repository))));

    private static Result<T, TriggerError> Read<T>(string text, Func<JsonElement, Result<T, TriggerError>> read)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes)
        {
            return TriggerError.TooLarge;
        }

        try
        {
            using var document = JsonDocument.Parse(text, Options);

            return document.RootElement.ValueKind == JsonValueKind.Object ? read(document.RootElement) : TriggerError.Malformed;
        }
        catch (JsonException)
        {
            return TriggerError.Malformed;
        }
    }

    private static Result<JsonElement, TriggerError> Fields(JsonElement entry, IReadOnlyCollection<string> allowed) =>
        entry.EnumerateObject().Any(field => !allowed.Contains(field.Name)) ? TriggerError.UnknownField : entry;

    private static Result<IReadOnlyList<string>, TriggerError> Listed(JsonElement root)
    {
        if (!root.TryGetProperty("repositories", out var listed))
        {
            return Result<IReadOnlyList<string>, TriggerError>.Success([]);
        }

        if (listed.ValueKind != JsonValueKind.Array || listed.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString())))
        {
            return TriggerError.Malformed;
        }

        return Result<IReadOnlyList<string>, TriggerError>.Success([.. listed.EnumerateArray().Select(item => Repositories.Key(item.GetString()!)).Distinct(StringComparer.Ordinal)]);
    }

    private static Result<IReadOnlyList<TriggerDeclaration>, TriggerError> Triggers(JsonElement root, Func<JsonElement, Result<TriggerDeclaration, TriggerError>> read)
    {
        if (!root.TryGetProperty("triggers", out var list))
        {
            return Result<IReadOnlyList<TriggerDeclaration>, TriggerError>.Success([]);
        }

        if (list.ValueKind != JsonValueKind.Array)
        {
            return TriggerError.Malformed;
        }

        var triggers = new List<TriggerDeclaration>();

        foreach (var entry in list.EnumerateArray())
        {
            if (!read(entry).TryGetValue(out var trigger, out var error))
            {
                return error;
            }

            triggers.Add(trigger);
        }

        return triggers.GroupBy(trigger => trigger.Id.Name, StringComparer.Ordinal).Any(same => same.Count() > 1)
            ? TriggerError.DuplicateKey
            : triggers;
    }

    private static Result<TriggerDeclaration, TriggerError> Trigger(JsonElement entry, string scope, Option<string> repository)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return TriggerError.Malformed;
        }

        if (repository.IsSome && entry.TryGetProperty("webhook", out _))
        {
            return TriggerError.WebhookNotAllowed;
        }

        return Fields(entry, repository.IsSome ? Common : [.. Common, "repository"])
            .Bind(_ => TriggerFields.Key(entry))
            .Bind(key => repository.Match(Result<string, TriggerError>.Success, () => TriggerFields.Repository(entry))
                .Bind(where => TriggerFields.Instruction(entry)
                    .Map(instruction => new TriggerDeclaration(new TriggerId(scope, key), where, instruction))))
            .Bind(trigger => Firing(entry, trigger))
            .Bind(trigger => Settings(entry, trigger));
    }

    private static Result<TriggerDeclaration, TriggerError> Firing(JsonElement entry, TriggerDeclaration trigger)
    {
        var scheduled = entry.TryGetProperty("schedule", out var schedule);
        var called = entry.TryGetProperty("webhook", out var webhook);

        if (scheduled == called)
        {
            return TriggerError.InvalidSchedule;
        }

        if (called && entry.TryGetProperty("catchUp", out _))
        {
            return TriggerError.InvalidCatchUp;
        }

        return scheduled
            ? TriggerFields.Schedule(schedule).Bind(read => TriggerFields.Optional(entry, "catchUp", CatchUp.None, value => TriggerFields.Word(value, CatchUps, TriggerError.InvalidCatchUp))
                .Map(catchUp => trigger with { Schedule = read, CatchUp = catchUp }))
            : TriggerFields.Webhook(webhook).Map(rule => trigger with { Webhook = rule });
    }

    private static Result<TriggerDeclaration, TriggerError> Settings(JsonElement entry, TriggerDeclaration trigger) =>
        TriggerFields.Optional(entry, "enabled", true, TriggerFields.Flag)
            .Bind(enabled => TriggerFields.Optional(entry, "target", TriggerTarget.Job, value => TriggerFields.Word(value, Targets, TriggerError.InvalidTarget))
            .Bind(target => TriggerFields.Optional(entry, "autonomy", Autonomy.Supervised, value => TriggerFields.Word(value, Levels, TriggerError.InvalidAutonomy))
            .Bind(autonomy => TriggerFields.Optional(entry, "attempts", TriggerDeclaration.DefaultAttempts, value => TriggerFields.Whole(value, 1, 10, TriggerError.InvalidAttempts))
            .Bind(attempts => TriggerFields.Optional(entry, "concurrency", 1, value => TriggerFields.Whole(value, 1, 16, TriggerError.InvalidConcurrency))
            .Bind(concurrency => Connection(entry).Map(connection => trigger with
            {
                Enabled = enabled,
                Target = target,
                Autonomy = autonomy,
                Attempts = attempts,
                Concurrency = concurrency,
                Connection = connection,
            }))))));

    private static Result<Option<ConnectionName>, TriggerError> Connection(JsonElement entry) =>
        !entry.TryGetProperty("connection", out var connection) ? Option<ConnectionName>.None
        : connection.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(connection.GetString()) ? Option<ConnectionName>.Some(new ConnectionName(connection.GetString()!))
        : TriggerError.Malformed;
}
