using System.Text;
using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;

namespace Avala.ClaudeCode.Conversations;

internal sealed class StreamTranslator(ToolBook tools, Places places)
{
    private const int OutputLength = 16 * 1024;

    private static readonly string[] CanvasFields = ["title", "mediaType", "content"];

    private readonly Dictionary<int, ItemId> blocks = [];
    private readonly Dictionary<int, (TrackedTool Tool, StringBuilder Json)> drafts = [];
    private readonly HashSet<string> streamed = new(StringComparer.Ordinal);
    private readonly List<ItemId> open = [];
    private readonly PlanBook plan = new();
    private string message = string.Empty;

    public Reaction Receive(JsonNode received, Stamp stamp) =>
        received.Text("parent_tool_use_id").Match(
            parent => Nested(received, parent, stamp),
            () => received.TextOr("type", string.Empty) switch
            {
                "stream_event" => Streamed(received.Members("event"), stamp),
                "assistant" => Said(received.Members("message"), stamp),
                "user" => Returned(received.Members("message"), stamp, nested: false),
                _ => Reaction.None,
            });

    public Reaction Close(Stamp stamp, bool interrupted)
    {
        var closing = open.Select(item => new ItemCompleted(stamp.Session, stamp.Turn, item, interrupted ? ItemOutcome.Cancelled : ItemOutcome.Succeeded))
            .Concat(tools.All.Where(tool => tool.Opened && !tool.Closed).Select(tool =>
            {
                tool.Closed = true;

                return new ItemCompleted(stamp.Session, stamp.Turn, tool.Item, interrupted || tool.Refused ? ItemOutcome.Cancelled : ItemOutcome.Failed);
            }))
            .ToList<IAgentEvent>();
        open.Clear();
        blocks.Clear();
        drafts.Clear();
        streamed.Clear();
        tools.Forget();

        return Reaction.Of(closing);
    }

    private Reaction Nested(JsonNode received, string parent, Stamp stamp) => received.TextOr("type", string.Empty) switch
    {
        "assistant" => received.Members("message").Items("content")
            .Select(block => block.TextOr("type", string.Empty) switch
            {
                "tool_use" => Used(new ToolUse(block.TextOr("id", string.Empty), block.TextOr("name", string.Empty), block.Members("input")), stamp, nested: true),
                "text" => Narrated(parent, block.TextOr("text", string.Empty), stamp),
                _ => Reaction.None,
            })
            .Aggregate(Reaction.None, (all, next) => all.Then(next)),
        "user" => Returned(received.Members("message"), stamp, nested: true),
        _ => Reaction.None,
    };

    private Reaction Narrated(string parent, string text, Stamp stamp) =>
        tools.Find(parent).Match(
            tool =>
            {
                if (!tool.Opened || tool.Closed || text.Length == 0)
                {
                    return Reaction.None;
                }

                var separated = tool.Narrated ? $"\n\n{text}" : text;
                tool.Narrated = true;

                return Reaction.Of(new ItemProgressed(stamp.Session, stamp.Turn, tool.Item, separated));
            },
            () => Reaction.None);

    private Reaction Streamed(JsonObject streamEvent, Stamp stamp)
    {
        var index = (int)streamEvent.Number("index");

        switch (streamEvent.TextOr("type", string.Empty))
        {
            case "message_start":
                message = streamEvent.Members("message").TextOr("id", string.Empty);
                streamed.Add(message);

                return Reaction.None;
            case "content_block_start":
                var block = streamEvent.Members("content_block");

                return block.TextOr("type", string.Empty) switch
                {
                    "text" => Open(index, ItemKind.Message, "Message", stamp),
                    "thinking" => Open(index, ItemKind.Reasoning, "Thinking", stamp),
                    "tool_use" => Drafted(index, block),
                    _ => Reaction.None,
                };
            case "content_block_delta" when drafts.TryGetValue(index, out var draft):
                draft.Json.Append(streamEvent.Members("delta").TextOr("partial_json", string.Empty));

                return Drawn(draft.Tool, PartialJson.Strings(draft.Json.ToString()), stamp);
            case "content_block_delta" when blocks.TryGetValue(index, out var item):
                var delta = streamEvent.Members("delta");
                var text = delta.TextOr("text", delta.TextOr("thinking", string.Empty));

                return text.Length == 0 ? Reaction.None : Reaction.Of(new ItemProgressed(stamp.Session, stamp.Turn, item, text));
            case "content_block_stop" when drafts.Remove(index):
                return Reaction.None;
            case "content_block_stop" when blocks.Remove(index, out var stopped):
                open.Remove(stopped);

                return Reaction.Of(new ItemCompleted(stamp.Session, stamp.Turn, stopped, ItemOutcome.Succeeded));
            default:
                return Reaction.None;
        }
    }

    private Reaction Drafted(int index, JsonObject block)
    {
        var use = new ToolUse(block.TextOr("id", string.Empty), block.TextOr("name", string.Empty), []);

        if (use.Id.Length > 0 && tools.Find(use.Id).IsNone && tools.Harness(use.Name).Match(tool => tool.Surface == ToolSurface.Canvas, () => false))
        {
            drafts[index] = (tools.Track(use), new StringBuilder());
        }

        return Reaction.None;
    }

    private static Reaction Drawn(TrackedTool tool, IReadOnlyDictionary<string, PartialText> input, Stamp stamp)
    {
        if (tool.Closed)
        {
            return Reaction.None;
        }

        var opening = Reaction.None;

        if (!tool.Opened)
        {
            if (!input.TryGetValue("title", out var title) || !title.Complete || !input.TryGetValue("mediaType", out var mediaType) || !mediaType.Complete)
            {
                return Reaction.None;
            }

            tool.Opened = true;
            opening = Reaction.Of(new CanvasStarted(stamp.Session, stamp.Turn, tool.Item, title.Value, mediaType.Value));
        }

        var content = input.TryGetValue("content", out var drawn) ? drawn.Value : string.Empty;

        if (content.Length <= tool.Drawn.Length || !content.StartsWith(tool.Drawn, StringComparison.Ordinal))
        {
            return opening;
        }

        var more = content[tool.Drawn.Length..];
        tool.Drawn = content;

        return opening.Then(Reaction.Of(new ItemProgressed(stamp.Session, stamp.Turn, tool.Item, more)));
    }

    private Reaction Open(int index, ItemKind kind, string title, Stamp stamp)
    {
        var item = new ItemId($"{message}:{index}");
        blocks[index] = item;
        open.Add(item);

        return Reaction.Of(new ItemStarted(stamp.Session, stamp.Turn, item, kind, title));
    }

    private Reaction Said(JsonObject said, Stamp stamp)
    {
        var id = said.TextOr("id", string.Empty);
        var whole = !streamed.Contains(id);

        return said.Items("content")
            .Select((block, index) => block.TextOr("type", string.Empty) switch
            {
                "tool_use" => Used(new ToolUse(block.TextOr("id", string.Empty), block.TextOr("name", string.Empty), block.Members("input")), stamp, nested: false),
                "text" when whole => Whole(new ItemId($"{id}:said:{index}"), ItemKind.Message, "Message", block.TextOr("text", string.Empty), stamp),
                "thinking" when whole => Whole(new ItemId($"{id}:said:{index}"), ItemKind.Reasoning, "Thinking", block.TextOr("thinking", string.Empty), stamp),
                _ => Reaction.None,
            })
            .Aggregate(Reaction.None, (all, next) => all.Then(next));
    }

    private static Reaction Whole(ItemId item, ItemKind kind, string title, string text, Stamp stamp) =>
        text.Length == 0
            ? Reaction.None
            : Reaction.Of(
                new ItemStarted(stamp.Session, stamp.Turn, item, kind, title),
                new ItemProgressed(stamp.Session, stamp.Turn, item, text),
                new ItemCompleted(stamp.Session, stamp.Turn, item, ItemOutcome.Succeeded));

    private Reaction Used(ToolUse use, Stamp stamp, bool nested)
    {
        if (use.Id.Length == 0)
        {
            return Reaction.None;
        }

        var known = tools.Find(use.Id);

        if (known.IsSome)
        {
            return known.Match(tracked => tracked.Role == ToolRole.Canvas ? Drawn(tracked, Final(use.Input), stamp) : Reaction.None, () => Reaction.None);
        }

        var tool = tools.Track(use);

        switch (tool.Role)
        {
            case ToolRole.Work:
                tool.Opened = true;

                return Reaction.Of(new ItemStarted(stamp.Session, stamp.Turn, tool.Item, use.KindIn(places), use.Heading(places)) { Input = use.Details(places.WorkingDirectory) });
            case ToolRole.Plan when !nested:
                return Planned(plan.Used(use, stamp));
            case ToolRole.Canvas:
                return Drawn(tool, Final(use.Input), stamp);
            default:
                return Reaction.None;
        }
    }

    private static Dictionary<string, PartialText> Final(JsonObject input) =>
        new(
            CanvasFields
                .Select(name => KeyValuePair.Create(name, new PartialText(input.TextOr(name, name == "title" ? "Canvas" : name == "mediaType" ? "text/plain" : string.Empty), true))),
            StringComparer.Ordinal);

    private static Reaction Planned(Option<PlanUpdated> updated) =>
        updated.Match(update => Reaction.Of(update), () => Reaction.None);

    private Reaction Returned(JsonObject returned, Stamp stamp, bool nested) =>
        returned.Items("content")
            .Where(block => block.TextOr("type", string.Empty) == "tool_result")
            .Select(block => tools.Find(block.TextOr("tool_use_id", string.Empty)).Match(
                tool => tool.Role == ToolRole.Plan
                    ? nested ? Reaction.None : Planned(plan.Returned(tool.Use.Id, block.Flag("is_error"), Output(block), stamp))
                    : Finished(tool, block, stamp),
                () => Reaction.None))
            .Aggregate(Reaction.None, (all, next) => all.Then(next));

    private static Reaction Finished(TrackedTool tool, JsonNode result, Stamp stamp)
    {
        if (!tool.Opened || tool.Closed)
        {
            return Reaction.None;
        }

        tool.Closed = true;
        var failed = result.Flag("is_error");
        var outcome = tool.Refused ? ItemOutcome.Cancelled : failed || tool.Role == ToolRole.Executed ? ItemOutcome.Failed : ItemOutcome.Succeeded;
        var output = tool.Role == ToolRole.Work && !tool.Refused && !tool.Narrated ? Output(result) : string.Empty;

        return Reaction.Of(
        [
            .. output.Length == 0 ? Array.Empty<IAgentEvent>() : [new ItemProgressed(stamp.Session, stamp.Turn, tool.Item, output)],
            new ItemCompleted(stamp.Session, stamp.Turn, tool.Item, outcome),
        ]);
    }

    private static string Output(JsonNode result)
    {
        var text = result.Text("content").Match(
            content => content,
            () => string.Join('\n', result.Items("content").Select(part => part.TextOr("text", part.TextOr("tool_name", string.Empty))).Where(part => part.Length > 0)));

        return text.Length <= OutputLength ? text : $"{text[..OutputLength]}…";
    }
}
