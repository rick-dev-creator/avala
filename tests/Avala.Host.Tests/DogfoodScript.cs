using System.Globalization;
using System.Text.Json.Nodes;
using Avala.Sdk;

namespace Avala.Host.Tests;

internal sealed class DogfoodScript(string name)
{
    private readonly JsonArray entries = [];
    private int items;

    public string Name => name;

    public string Tag => $"[replay: {name}]";

    public string Json => new JsonObject
    {
        ["format"] = "avala-recording",
        ["version"] = 1,
        ["options"] = new JsonObject { ["permissions"] = "askEveryTime" },
        ["entries"] = entries.DeepClone(),
    }.ToJsonString();

    public DogfoodScript Turn() =>
        Event("turnStarted").Event("resumeTokenIssued", new JsonObject { ["token"] = $"{name}-{entries.Count}" });

    public DogfoodScript Say(string text)
    {
        var item = Next("reply");

        return Event("itemStarted", Item(item, "message", "Reply"))
            .Event("itemProgressed", new JsonObject { ["item"] = item, ["text"] = text })
            .Event("itemCompleted", new JsonObject { ["item"] = item, ["outcome"] = "succeeded" });
    }

    public DogfoodScript Write(string path, string content)
    {
        var item = Next("edit");
        Event("itemStarted", Item(item, "fileEdit", $"Edit {path}"));
        entries.Add(new JsonObject { ["at"] = 0, ["file"] = new JsonObject { ["path"] = path, ["content"] = content } });

        return Event("itemCompleted", new JsonObject { ["item"] = item, ["outcome"] = "succeeded" });
    }

    public DogfoodScript Run(string command, string output) => Command(command, output, asks: false);

    public DogfoodScript Ask(string command, string output) => Command(command, output, asks: true);

    public DogfoodScript Bill(decimal cost, double used) => Bill(cost, used, "5h", Option<DateTimeOffset>.None);

    public DogfoodScript Bill(decimal cost, double used, string window, Option<DateTimeOffset> resets)
    {
        var limit = new JsonObject { ["window"] = window, ["usedFraction"] = used };

        foreach (var at in resets.Match<DateTimeOffset[]>(found => [found], () => []))
        {
            limit["resetsAt"] = at.ToString("O", CultureInfo.InvariantCulture);
        }

        return Event("usageReported", new JsonObject
            {
                ["tokens"] = new JsonObject { ["input"] = 2_400, ["output"] = 180, ["cacheRead"] = 1_200, ["cacheWrite"] = 240, ["reasoning"] = 0 },
                ["cost"] = new JsonObject { ["amount"] = cost, ["currency"] = "USD" },
            })
            .Event("limitReported", new JsonObject { ["limit"] = limit });
    }

    public DogfoodScript Call(string tool, string input, bool refused)
    {
        var item = Next("call");
        Event("toolCalled", new JsonObject { ["item"] = item, ["tool"] = tool, ["input"] = input });
        entries.Add(new JsonObject { ["at"] = 0, ["return"] = new JsonObject { ["item"] = item, ["content"] = string.Empty, ["isError"] = refused } });

        return Event("toolReturned", new JsonObject { ["item"] = item, ["result"] = new JsonObject { ["item"] = item, ["content"] = string.Empty, ["isError"] = refused } });
    }

    public DogfoodScript End(string outcome) => Event("turnCompleted", new JsonObject { ["outcome"] = outcome });

    public DogfoodScript Interrupt()
    {
        entries.Add(new JsonObject { ["at"] = 0, ["interrupt"] = new JsonObject() });

        return this;
    }

    public DogfoodScript Finish() => Event("turnCompleted", new JsonObject { ["outcome"] = "finished" });

    private DogfoodScript Command(string command, string output, bool asks)
    {
        var item = Next("command");
        Event("itemStarted", Item(item, "command", $"Run {command}"));

        if (asks)
        {
            Event("permissionRequested", new JsonObject { ["item"] = item, ["title"] = $"Run {command}", ["kind"] = "command", ["target"] = command });
            entries.Add(new JsonObject { ["at"] = 0, ["respond"] = new JsonObject { ["item"] = item, ["answer"] = "allow" } });
            Event("permissionResolved", new JsonObject { ["item"] = item, ["answer"] = "allow" });
        }

        return Event("itemProgressed", new JsonObject { ["item"] = item, ["text"] = output })
            .Event("itemCompleted", new JsonObject { ["item"] = item, ["outcome"] = "succeeded" });
    }

    private DogfoodScript Event(string type, JsonObject? fields = null)
    {
        var happened = new JsonObject { ["type"] = type };

        foreach (var (key, value) in fields ?? [])
        {
            happened[key] = value?.DeepClone();
        }

        entries.Add(new JsonObject { ["at"] = 0, ["event"] = happened });

        return this;
    }

    private string Next(string kind) => $"{kind}-{++items}";

    private static JsonObject Item(string item, string kind, string title) => new() { ["item"] = item, ["kind"] = kind, ["title"] = title };
}
