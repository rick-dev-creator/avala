using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avala.Sdk;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Triggers.Declarations;

namespace Avala.Triggers.TriggerFiles;

internal static partial class TriggerFields
{
    public const int LongestInstruction = 16_000;

    private const int MostMinutes = 525_600;

    private const int MostRate = 3_600;

    private static readonly Dictionary<string, DayOfWeek> Weekdays = new(StringComparer.Ordinal)
    {
        ["mon"] = DayOfWeek.Monday,
        ["tue"] = DayOfWeek.Tuesday,
        ["wed"] = DayOfWeek.Wednesday,
        ["thu"] = DayOfWeek.Thursday,
        ["fri"] = DayOfWeek.Friday,
        ["sat"] = DayOfWeek.Saturday,
        ["sun"] = DayOfWeek.Sunday,
    };

    public static Result<T, TriggerError> Optional<T>(JsonElement entry, string name, T fallback, Func<JsonElement, Result<T, TriggerError>> read)
        where T : notnull =>
        entry.TryGetProperty(name, out var value) ? read(value) : fallback;

    public static Result<string, TriggerError> Key(JsonElement entry) =>
        entry.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && id.GetString() is { } key && KeyPattern().IsMatch(key)
            ? key
            : TriggerError.InvalidKey;

    public static Result<string, TriggerError> Repository(JsonElement entry) =>
        !entry.TryGetProperty("repository", out var repository) ? TriggerError.MissingRepository
        : repository.ValueKind != JsonValueKind.String ? TriggerError.Malformed
        : repository.GetString() is { } path && !string.IsNullOrWhiteSpace(path) ? Repositories.Key(path)
        : TriggerError.MissingRepository;

    public static Result<InstructionTemplate, TriggerError> Instruction(JsonElement entry)
    {
        if (!entry.TryGetProperty("instruction", out var instruction))
        {
            return TriggerError.MissingInstruction;
        }

        if (instruction.ValueKind != JsonValueKind.String)
        {
            return TriggerError.Malformed;
        }

        return instruction.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text.Length > LongestInstruction ? TriggerError.InvalidTemplate : InstructionTemplate.Parse(text).ToResult(TriggerError.InvalidTemplate)
            : TriggerError.MissingInstruction;
    }

    public static Result<bool, TriggerError> Flag(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => TriggerError.Malformed,
        };

    public static Result<int, TriggerError> Whole(JsonElement value, int least, int most, TriggerError outOfRange) =>
        value.ValueKind != JsonValueKind.Number ? TriggerError.Malformed
        : value.TryGetInt32(out var number) && number >= least && number <= most ? number
        : outOfRange;

    public static Result<T, TriggerError> Word<T>(JsonElement value, IReadOnlyDictionary<string, T> words, TriggerError unknown)
        where T : notnull =>
        value.ValueKind != JsonValueKind.String ? TriggerError.Malformed
        : words.TryGetValue(value.GetString() ?? string.Empty, out var word) ? word
        : unknown;

    public static Result<TriggerSchedule, TriggerError> Schedule(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return TriggerError.Malformed;
        }

        var names = value.EnumerateObject().Select(field => field.Name).ToList();

        if (names.Exists(name => name is not ("everyMinutes" or "at" or "days")))
        {
            return TriggerError.UnknownField;
        }

        return names switch
        {
            ["everyMinutes"] => Whole(value.GetProperty("everyMinutes"), 1, MostMinutes, TriggerError.InvalidInterval).Map(TriggerSchedule.Every),
            _ when names.Contains("at") && !names.Contains("everyMinutes") => Time(value.GetProperty("at"))
                .Bind(at => Optional<IReadOnlyList<DayOfWeek>>(value, "days", [], Days).Map(days => TriggerSchedule.Daily(at, days))),
            _ => TriggerError.InvalidSchedule,
        };
    }

    public static Result<WebhookRule, TriggerError> Webhook(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return TriggerError.Malformed;
        }

        if (value.EnumerateObject().Any(field => field.Name is not ("secretEnv" or "ratePerHour")))
        {
            return TriggerError.UnknownField;
        }

        var secret = value.TryGetProperty("secretEnv", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() is { } variable && VariablePattern().IsMatch(variable)
            ? Result<string, TriggerError>.Success(variable)
            : TriggerError.InvalidSecret;

        return secret.Bind(variable => Optional(value, "ratePerHour", WebhookRule.DefaultRate, rate => Whole(rate, 1, MostRate, TriggerError.InvalidRate))
            .Map(rate => new WebhookRule(variable, rate)));
    }

    private static Result<TimeOnly, TriggerError> Time(JsonElement value) =>
        value.ValueKind != JsonValueKind.String ? TriggerError.Malformed
        : TimeOnly.TryParseExact(value.GetString(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at
        : TriggerError.InvalidTime;

    private static Result<IReadOnlyList<DayOfWeek>, TriggerError> Days(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return TriggerError.Malformed;
        }

        var names = value.EnumerateArray().Select(day => day.ValueKind == JsonValueKind.String ? day.GetString() ?? string.Empty : string.Empty).ToList();

        return names.Count == 0 || names.Exists(day => !Weekdays.ContainsKey(day)) || names.Distinct(StringComparer.Ordinal).Count() != names.Count
            ? TriggerError.InvalidDays
            : Result<IReadOnlyList<DayOfWeek>, TriggerError>.Success([.. names.Select(day => Weekdays[day])]);
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,127}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();
}
