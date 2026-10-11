using System.Buffers;
using System.Text;
using System.Text.Json;
using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Triggers.Contracts;

namespace Avala.Forges.Sifting;

internal static class EventPayload
{
    public static string NameOf(ForgeEventKind kind) =>
        kind switch
        {
            ForgeEventKind.IssueOpened => "issue-opened",
            ForgeEventKind.IssueLabeled => "issue-labeled",
            ForgeEventKind.CommentCommand => "comment-command",
            ForgeEventKind.ChecksFailed => "checks-failed",
            _ => "review-requested",
        };

    public static string KeyOf(ForgeRule rule, ForgeEvent happened) => $"forge:{rule.Connection}:{NameOf(happened.Kind)}:{happened.Id}";

    public static string Json(ForgeRule rule, ForgeEvent happened)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("event", NameOf(happened.Kind));
            writer.WriteString("connection", rule.Connection);
            writer.WriteString("id", happened.Id);
            writer.WriteNumber("number", happened.Number);
            writer.WriteString("title", happened.Title);
            writer.WriteString("body", happened.Body);
            writer.WriteString("url", happened.Link.Match(link => link.AbsoluteUri, () => string.Empty));
            writer.WriteString("author", happened.Author);
            writer.WriteString("actor", happened.Actor);
            writer.WriteString("label", Text(happened.Label));
            writer.WriteString("comment", Text(happened.Comment));
            writer.WriteString("branch", Text(happened.Branch));
            writer.WriteString("head", Text(happened.Head));
            writer.WriteString("reviewer", Text(happened.Reviewer));
            writer.WriteBoolean("pullRequest", happened.PullRequest);
            writer.WriteStartArray("checks");

            foreach (var check in happened.Checks)
            {
                writer.WriteStartObject();
                writer.WriteString("name", check.Name);
                writer.WriteString("status", check.Status.ToString().ToLowerInvariant());
                writer.WriteString("summary", check.Summary);
                writer.WriteString("url", check.Link.Match(link => link.AbsoluteUri, () => string.Empty));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string Text(Option<string> text) => text.Match(found => found, () => string.Empty);
}
