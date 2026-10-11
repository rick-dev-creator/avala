using System.Text.Json;
using Avala.Sdk;
using Avala.Triggers.Contracts;
using Avala.Triggers.Declarations;

namespace Avala.Triggers.TriggerFiles;

internal sealed record ForgeDeclaration(ForgeRule Rule, Option<WebhookRule> Nudge);

internal static class ForgeFields
{
    private const string Label = "label";

    private const string Command = "command";

    private const string Branch = "branch";

    private const string Reviewer = "reviewer";

    private static readonly string[] Allowed = ["connection", "event", "authors", Label, Command, Branch, Reviewer, "remote", "acknowledge"];

    private static readonly Dictionary<string, ForgeEventKind> Events = new(StringComparer.Ordinal)
    {
        ["issue-opened"] = ForgeEventKind.IssueOpened,
        ["issue-labeled"] = ForgeEventKind.IssueLabeled,
        ["comment-command"] = ForgeEventKind.CommentCommand,
        ["checks-failed"] = ForgeEventKind.ChecksFailed,
        ["review-requested"] = ForgeEventKind.ReviewRequested,
    };

    private static readonly Dictionary<ForgeEventKind, (Option<string> Required, Option<string> Optional)> Filters = new()
    {
        [ForgeEventKind.IssueOpened] = (Option<string>.None, Option<string>.None),
        [ForgeEventKind.IssueLabeled] = (Label, Option<string>.None),
        [ForgeEventKind.CommentCommand] = (Command, Option<string>.None),
        [ForgeEventKind.ChecksFailed] = (Branch, Option<string>.None),
        [ForgeEventKind.ReviewRequested] = (Option<string>.None, Reviewer),
    };

    public static Result<ForgeDeclaration, TriggerError> Read(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            return TriggerError.Malformed;
        }

        if (value.EnumerateObject().Any(field => !Allowed.Contains(field.Name)))
        {
            return TriggerError.UnknownField;
        }

        return Text(value, "connection").Bind(connection => connection.ToResult(TriggerError.Malformed))
            .Bind(connection => Event(value)
            .Bind(kind => Filtered(value, kind)
            .Bind(filters => Authors(value)
            .Bind(authors => Text(value, "remote")
            .Bind(remote => TriggerFields.Optional(value, "acknowledge", false, TriggerFields.Flag)
            .Map(acknowledge => new ForgeDeclaration(
                new ForgeRule(connection, kind, authors)
                {
                    Remote = remote.Match(name => name, () => ForgeRule.DefaultRemote),
                    Label = filters.GetValueOrDefault(Label, Option<string>.None),
                    Command = filters.GetValueOrDefault(Command, Option<string>.None),
                    Branch = filters.GetValueOrDefault(Branch, Option<string>.None),
                    Reviewer = filters.GetValueOrDefault(Reviewer, Option<string>.None),
                    Acknowledge = acknowledge,
                },
                Option<WebhookRule>.None)))))));
    }

    private static Result<ForgeEventKind, TriggerError> Event(JsonElement value) =>
        value.TryGetProperty("event", out var kind) ? TriggerFields.Word(kind, Events, TriggerError.InvalidEvent) : TriggerError.InvalidEvent;

    private static Result<Dictionary<string, Option<string>>, TriggerError> Filtered(JsonElement value, ForgeEventKind kind)
    {
        var (required, optional) = Filters[kind];
        var found = new Dictionary<string, Option<string>>(StringComparer.Ordinal);

        foreach (var name in new[] { Label, Command, Branch, Reviewer })
        {
            var allowed = required.Match(field => field == name, () => false) || optional.Match(field => field == name, () => false);

            if (value.TryGetProperty(name, out _) && !allowed)
            {
                return TriggerError.UnknownField;
            }

            if (!Text(value, name).TryGetValue(out var text, out var error))
            {
                return error;
            }

            found[name] = text;
        }

        return required.Match(field => found[field].IsSome, () => true) ? found : TriggerError.InvalidEvent;
    }

    private static Result<IReadOnlyList<string>, TriggerError> Authors(JsonElement value)
    {
        if (!value.TryGetProperty("authors", out var authors))
        {
            return TriggerError.MissingAuthors;
        }

        if (authors.ValueKind != JsonValueKind.Array || authors.EnumerateArray().Any(author => author.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(author.GetString())))
        {
            return TriggerError.Malformed;
        }

        IReadOnlyList<string> names = [.. authors.EnumerateArray().Select(author => author.GetString()!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)];

        return names.Count == 0 ? TriggerError.MissingAuthors : Result<IReadOnlyList<string>, TriggerError>.Success(names);
    }

    private static Result<Option<string>, TriggerError> Text(JsonElement value, string name) =>
        !value.TryGetProperty(name, out var field) ? Option<string>.None
        : field.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(field.GetString()) ? Option<string>.Some(field.GetString()!.Trim())
        : TriggerError.Malformed;
}
