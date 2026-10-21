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
    private readonly Dictionary<int, SketchedTool> sketches = [];
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
                "assistant" => Said(received.Members("message"), received.Flag("is_api_error_message"), stamp),
                "user" => Returned(received.Members("message"), stamp, nested: false, received.Members("tool_use_result")),
                _ => Reaction.None,
            });

    public bool WorkingInBackground => tools.WorkingInBackground;

    public Reaction Close(Stamp stamp, bool interrupted) => Close(stamp, interrupted, ending: true);

    public Reaction Carry(Stamp stamp) => Close(stamp, interrupted: false, ending: false);

    public Reaction Tasked(JsonNode system, Stamp stamp) => system.TextOr("subtype", string.Empty) switch
    {
        "task_started" when system.Flag("is_backgrounded") => Started(system, stamp),
        "task_notification" => Notifying(system).Match(
            tool => tool.Backgrounded && !tool.Closed ? Notified(tool, system, stamp) : Reaction.None,
            () => tools.Finished(system.TextOr("tool_use_id", string.Empty), system.TextOr("task_id", string.Empty)) ? Reaction.None : Orphaned(system, stamp)),
        _ => Reaction.None,
    };

    private Reaction Close(Stamp stamp, bool interrupted, bool ending)
    {
        var closing = open.Select(item => new ItemCompleted(stamp.Session, stamp.Turn, item, interrupted ? ItemOutcome.Cancelled : ItemOutcome.Succeeded))
            .Concat(tools.All.Where(tool => tool.Opened && !tool.Closed && (ending ? !tool.Backgrounded || interrupted : !tools.Carries(tool))).Select(tool =>
            {
                tool.Closed = true;

                return new ItemCompleted(stamp.Session, stamp.Turn, tool.Item, interrupted || tool.Refused ? ItemOutcome.Cancelled : ItemOutcome.Failed);
            }))
            .ToList<IAgentEvent>();
        open.Clear();
        blocks.Clear();
        drafts.Clear();
        sketches.Clear();
        streamed.Clear();

        if (ending)
        {
            tools.ForgetAll();
        }
        else
        {
            tools.Forget();
        }

        return Reaction.Of(closing);
    }

    public bool Wakes(JsonNode notification) =>
        !tools.Finished(notification.TextOr("tool_use_id", string.Empty), notification.TextOr("task_id", string.Empty))
        || tools.Find(notification.TextOr("tool_use_id", string.Empty)).Match(tool => !tool.Closed, () => false);

    private Reaction Started(JsonNode system, Stamp stamp)
    {
        var id = system.TextOr("tool_use_id", string.Empty);
        var task = system.TextOr("task_id", string.Empty);

        return tools.Find(id).Match(tool => !tool.Closed, () => false)
            ? tools.Find(id).Match(tool => Backgrounded(tool, task, stamp), () => Reaction.None)
            : tools.Rerun(id).Match(
                again => Reaction.Of(
                    new ItemStarted(stamp.Session, stamp.Turn, again.Item, again.Use.KindIn(places), again.Use.Heading(places)) { Input = again.Use.Details(places.WorkingDirectory) },
                    new ItemBackgrounded(stamp.Session, stamp.Turn, again.Item)),
                () => Reaction.None);
    }

    private Option<TrackedTool> Notifying(JsonNode notification) =>
        tools.Find(notification.TextOr("tool_use_id", string.Empty)).Match(Option<TrackedTool>.Some, () => tools.OfTask(notification.TextOr("task_id", string.Empty)));

    private static Reaction Backgrounded(TrackedTool tool, string task, Stamp stamp)
    {
        tool.Task = tool.Task.Length == 0 ? task : tool.Task;

        if (!tool.Opened || tool.Closed || tool.Backgrounded)
        {
            return Reaction.None;
        }

        tool.Backgrounded = true;

        return Reaction.Of(new ItemBackgrounded(stamp.Session, stamp.Turn, tool.Item));
    }

    private static Reaction Notified(TrackedTool tool, JsonNode notification, Stamp stamp)
    {
        tool.Closed = true;
        var summary = notification.TextOr("summary", string.Empty);

        return Reaction.Of(
        [
            .. summary.Length == 0 ? Array.Empty<IAgentEvent>() : [new ItemProgressed(stamp.Session, stamp.Turn, tool.Item, summary)],
            new ItemCompleted(stamp.Session, stamp.Turn, tool.Item, Ended(notification)),
        ]);
    }

    private static Reaction Orphaned(JsonNode notification, Stamp stamp)
    {
        var summary = notification.TextOr("summary", string.Empty);
        var task = notification.TextOr("task_id", string.Empty);

        return task.Length == 0
            ? Reaction.None
            : Whole(new ItemId($"task:{task}"), ItemKind.Subagent, "Background task", summary.Length == 0 ? $"Background task {task} ended" : summary, stamp, Ended(notification));
    }

    private static ItemOutcome Ended(JsonNode notification) => notification.TextOr("status", string.Empty) switch
    {
        "completed" => ItemOutcome.Succeeded,
        "failed" => ItemOutcome.Failed,
        _ => ItemOutcome.Cancelled,
    };

    public void Recall(JsonNode earlier)
    {
        if (earlier.Text("parent_tool_use_id").IsSome || earlier.Flag("isSidechain"))
        {
            return;
        }

        var content = earlier.Members("message").Items("content");

        switch (earlier.TextOr("type", string.Empty))
        {
            case "assistant":
                foreach (var block in content.Where(part => part.TextOr("type", string.Empty) == "tool_use" && PlanBook.Plans(part.TextOr("name", string.Empty))))
                {
                    plan.Use(new ToolUse(block.TextOr("id", string.Empty), block.TextOr("name", string.Empty), block.Members("input")));
                }

                break;
            case "user":
                foreach (var block in content.Where(part => part.TextOr("type", string.Empty) == "tool_result"))
                {
                    plan.Return(block.TextOr("tool_use_id", string.Empty), block.Flag("is_error"), Text(block));
                }

                break;
        }
    }

    private Reaction Nested(JsonNode received, string parent, Stamp stamp) => received.TextOr("type", string.Empty) switch
    {
        "assistant" => received.Members("message").Items("content")
            .Select(block => block.TextOr("type", string.Empty) switch
            {
                "tool_use" => Used(new ToolUse(block.TextOr("id", string.Empty), block.TextOr("name", string.Empty), block.Members("input")), stamp, new ItemId(parent)),
                "text" => Narrated(parent, block.TextOr("text", string.Empty), stamp),
                _ => Reaction.None,
            })
            .Aggregate(Reaction.None, (all, next) => all.Then(next))
            .Then(Metered(parent, received.Members("message").Members("usage"), stamp)),
        "user" => Returned(received.Members("message"), stamp, nested: true, received.Members("tool_use_result")),
        _ => Reaction.None,
    };

    private Reaction Metered(string parent, JsonObject usage, Stamp stamp) =>
        tools.Find(parent).Match(
            tool =>
            {
                var used = usage.Number("input_tokens") + usage.Number("cache_read_input_tokens") + usage.Number("cache_creation_input_tokens") + usage.Number("output_tokens");

                if (!tool.Opened || tool.Closed || used <= 0 || used == tool.Tokens)
                {
                    return Reaction.None;
                }

                tool.Tokens = used;

                return Reaction.Of(new ItemTokensReported(stamp.Session, stamp.Turn, tool.Item, used));
            },
            () => Reaction.None);

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

        return streamEvent.TextOr("type", string.Empty) switch
        {
            "message_start" => MessageStarted(streamEvent.Members("message")),
            "content_block_start" => BlockStarted(index, streamEvent.Members("content_block"), stamp),
            "content_block_delta" => BlockGrew(index, streamEvent.Members("delta"), stamp),
            "content_block_stop" => BlockStopped(index, stamp),
            _ => Reaction.None,
        };
    }

    private Reaction MessageStarted(JsonObject started)
    {
        message = started.TextOr("id", string.Empty);
        streamed.Add(message);

        return Reaction.None;
    }

    private Reaction BlockStarted(int index, JsonObject block, Stamp stamp) =>
        block.TextOr("type", string.Empty) switch
        {
            "text" => Open(index, ItemKind.Message, "Message", stamp),
            "thinking" => Open(index, ItemKind.Reasoning, "Thinking", stamp),
            "tool_use" => Drafted(index, block),
            _ => Reaction.None,
        };

    private Reaction BlockGrew(int index, JsonObject delta, Stamp stamp)
    {
        if (drafts.TryGetValue(index, out var draft))
        {
            draft.Json.Append(delta.TextOr("partial_json", string.Empty));

            return Drawn(draft.Tool, PartialJson.Strings(draft.Json.ToString()), stamp);
        }

        if (sketches.TryGetValue(index, out var sketch))
        {
            sketch.Json.Append(delta.TextOr("partial_json", string.Empty));

            return Sketched(sketch, stamp);
        }

        return blocks.TryGetValue(index, out var item) ? Progressed(item, delta.TextOr("text", delta.TextOr("thinking", string.Empty)), stamp) : Reaction.None;
    }

    private static Reaction Progressed(ItemId item, string text, Stamp stamp) =>
        text.Length == 0 ? Reaction.None : Reaction.Of(new ItemProgressed(stamp.Session, stamp.Turn, item, text));

    private Reaction BlockStopped(int index, Stamp stamp)
    {
        if (sketches.Remove(index) || drafts.Remove(index) || !blocks.Remove(index, out var stopped))
        {
            return Reaction.None;
        }

        open.Remove(stopped);

        return Reaction.Of(new ItemCompleted(stamp.Session, stamp.Turn, stopped, ItemOutcome.Succeeded));
    }

    private Reaction Drafted(int index, JsonObject block)
    {
        var use = new ToolUse(block.TextOr("id", string.Empty), block.TextOr("name", string.Empty), []);

        if (use.Id.Length > 0 && tools.Find(use.Id).IsNone && tools.Harness(use.Name).Match(tool => tool.Surface == ToolSurface.Canvas, () => false))
        {
            drafts[index] = (tools.Track(use), new StringBuilder());
        }
        else if (use.Id.Length > 0 && tools.Find(use.Id).IsNone && tools.Harness(use.Name).IsNone && use.Kind == ItemKind.FileEdit)
        {
            sketches[index] = new SketchedTool(use);
        }

        return Reaction.None;
    }

    private Reaction Sketched(SketchedTool sketch, Stamp stamp)
    {
        if (sketch.Abandoned)
        {
            return Reaction.None;
        }

        if (sketch.Known.IsNone)
        {
            var input = PartialJson.Strings(sketch.Json.ToString());
            var path = SketchedTool.Paths.FirstOrDefault(name => input.TryGetValue(name, out var found) && found.Complete);

            if (path is null)
            {
                return Reaction.None;
            }

            var known = sketch.Use with { Input = new JsonObject { [path] = input[path].Value } };
            sketch.Abandoned = known.WritesPlan(places);
            sketch.Known = known;

            return sketch.Abandoned ? Reaction.None : Draft(known, sketch, stamp);
        }

        return sketch.Json.Length / SketchedTool.Stride > sketch.Reported / SketchedTool.Stride
            ? sketch.Known.Match(known => Draft(known, sketch, stamp), () => Reaction.None)
            : Reaction.None;
    }

    private Reaction Draft(ToolUse known, SketchedTool sketch, Stamp stamp)
    {
        sketch.Reported = sketch.Json.Length;

        return Reaction.Of(new ItemDrafted(stamp.Session, stamp.Turn, new ItemId(known.Id), known.KindIn(places), known.Heading(places), sketch.Json.Length));
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

    private Reaction Said(JsonObject said, bool apiError, Stamp stamp)
    {
        var id = said.TextOr("id", string.Empty);
        var whole = !streamed.Contains(id);

        return said.Items("content")
            .Select((block, index) => block.TextOr("type", string.Empty) switch
            {
                "tool_use" => Used(new ToolUse(block.TextOr("id", string.Empty), block.TextOr("name", string.Empty), block.Members("input")), stamp, Option<ItemId>.None),
                "text" when apiError => Whole(new ItemId($"{id}:said:{index}"), ItemKind.Other, "Claude Code error", block.TextOr("text", string.Empty), stamp, ItemOutcome.Failed),
                "text" when whole => Whole(new ItemId($"{id}:said:{index}"), ItemKind.Message, "Message", block.TextOr("text", string.Empty), stamp),
                "thinking" when whole => Whole(new ItemId($"{id}:said:{index}"), ItemKind.Reasoning, "Thinking", block.TextOr("thinking", string.Empty), stamp),
                _ => Reaction.None,
            })
            .Aggregate(Reaction.None, (all, next) => all.Then(next));
    }

    private static Reaction Whole(ItemId item, ItemKind kind, string title, string text, Stamp stamp, ItemOutcome outcome = ItemOutcome.Succeeded) =>
        text.Length == 0
            ? Reaction.None
            : Reaction.Of(
                new ItemStarted(stamp.Session, stamp.Turn, item, kind, title),
                new ItemProgressed(stamp.Session, stamp.Turn, item, text),
                new ItemCompleted(stamp.Session, stamp.Turn, item, outcome));

    private Reaction Used(ToolUse use, Stamp stamp, Option<ItemId> parent)
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
        tool.Nested = parent.IsSome;

        switch (tool.Role)
        {
            case ToolRole.Work:
                tool.Opened = true;

                return Reaction.Of(new ItemStarted(stamp.Session, stamp.Turn, tool.Item, use.KindIn(places), use.Heading(places)) { Input = use.Details(places.WorkingDirectory), Parent = parent });
            case ToolRole.Plan when parent.IsNone:
                return Planned(plan.Use(use), stamp);
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

    private Reaction Planned(bool changed, Stamp stamp) =>
        changed ? Reaction.Of(plan.Current(stamp)) : Reaction.None;

    private Reaction Returned(JsonObject returned, Stamp stamp, bool nested, JsonObject reported) =>
        returned.Items("content")
            .Where(block => block.TextOr("type", string.Empty) == "tool_result")
            .Select(block => tools.Find(block.TextOr("tool_use_id", string.Empty)).Match(
                tool => tool.Role == ToolRole.Plan
                    ? nested ? Reaction.None : Planned(plan.Return(tool.Use.Id, block.Flag("is_error"), Text(block)), stamp)
                    : Stopping(tool, block, stamp).Then(Finished(tool, block, stamp, reported)),
                () => Reaction.None))
            .Aggregate(Reaction.None, (all, next) => all.Then(next));

    private Reaction Stopping(TrackedTool stopper, JsonNode result, Stamp stamp) =>
        stopper.Use.Stops && !result.Flag("is_error")
            ? tools.OfTask(stopper.Use.Input.TextOr("task_id", stopper.Use.Input.TextOr("shell_id", string.Empty))).Match(
                stopped =>
                {
                    if (!stopped.Backgrounded || stopped.Closed)
                    {
                        return Reaction.None;
                    }

                    stopped.Closed = true;

                    return Reaction.Of(new ItemCompleted(stamp.Session, stamp.Turn, stopped.Item, ItemOutcome.Cancelled));
                },
                () => Reaction.None)
            : Reaction.None;

    private static bool Launched(JsonObject outcome) =>
        outcome.TextOr("status", string.Empty) == "async_launched" || outcome.Text("backgroundTaskId").IsSome;

    private static Reaction Finished(TrackedTool tool, JsonNode result, Stamp stamp, JsonObject reported)
    {
        if (!tool.Opened || tool.Closed || tool.Backgrounded)
        {
            return Reaction.None;
        }

        if (tool.Role == ToolRole.Work && Launched(reported))
        {
            return Backgrounded(tool, reported.TextOr("agentId", reported.TextOr("backgroundTaskId", string.Empty)), stamp);
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

    private static string Output(JsonNode result) =>
        Text(result) is var text && text.Length <= OutputLength ? text : $"{text[..OutputLength]}…";

    private static string Text(JsonNode result) =>
        result.Text("content").Match(
            content => content,
            () => string.Join('\n', result.Items("content").Select(part => part.TextOr("text", part.TextOr("tool_name", string.Empty))).Where(part => part.Length > 0)));
}
