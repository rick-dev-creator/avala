using System.Text.Json.Nodes;
using Avala.ClaudeCode.Protocol;

namespace Avala.ClaudeCode.Conversations;

internal sealed class Commands
{
    private readonly HashSet<string> waiting = new(StringComparer.Ordinal);
    private bool tracked;
    private bool running;
    private int queued;

    public bool Pending => tracked ? waiting.Count > 0 : queued > 0;

    public bool Owed => !tracked || running || waiting.Count > 0;

    public string Send(bool midTurn)
    {
        var command = Guid.NewGuid().ToString();
        waiting.Add(command);
        queued += midTurn ? 1 : 0;

        return command;
    }

    public void Report(JsonNode lifecycle)
    {
        var command = lifecycle.TextOr("command_uuid", string.Empty);
        var state = lifecycle.TextOr("state", string.Empty);
        tracked = true;

        if (state == "queued")
        {
            return;
        }

        waiting.Remove(command);
        running |= state == "started";
    }

    public void Answered()
    {
        running = false;
        queued = Math.Max(0, queued - 1);
    }

    public int Abandon()
    {
        var left = tracked ? waiting.Count : queued;
        waiting.Clear();
        queued = 0;
        running = false;

        return left;
    }
}
