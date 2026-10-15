using System.Text.Json.Nodes;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Protocol;
using Avala.Sdk;

namespace Avala.ClaudeCode.Conversations;

internal sealed class RemoteBridge(SessionId session)
{
    public const string RequestPrefix = "avala-remote-";

    private RemoteControl control = RemoteControl.Off;
    private int requests;

    public RemoteState State => control.State;

    public static bool Answers(JsonNode response) =>
        response.Members("response").TextOr("request_id", string.Empty).StartsWith(RequestPrefix, StringComparison.Ordinal);

    public static bool FromDevice(JsonNode user) =>
        user.Flag("isReplay") && user.Members("origin").TextOr("kind", string.Empty) == "human";

    public static string Said(JsonNode user) =>
        user.Members("message").Text("content").Match(
            text => text,
            () => string.Join('\n', user.Members("message").Items("content").Where(part => part.TextOr("type", string.Empty) == "text").Select(part => part.TextOr("text", string.Empty))));

    public Reaction Turn(bool on) =>
        on == control.IsOn
            ? Reaction.None
            : Reaction.Send(Messages.RemoteControl(on, $"{RequestPrefix}{++requests}")).Then(Changed(on ? new RemoteControl(RemoteState.Starting) : RemoteControl.Off));

    public Reaction Answered(JsonNode response)
    {
        var body = response.Members("response");

        if (!control.IsOn)
        {
            return Reaction.None;
        }

        return body.TextOr("subtype", string.Empty) == "success"
            ? body.Members("response").Text("session_url").Match(link => Changed(control with { Link = link }), () => Reaction.None)
            : Changed(new RemoteControl(RemoteState.Failed) { Problem = body.TextOr("error", "Claude Code could not start Remote Control.") });
    }

    public Reaction Reported(JsonNode bridge)
    {
        if (!control.IsOn)
        {
            return Reaction.None;
        }

        return bridge.TextOr("state", string.Empty) switch
        {
            "connected" => Changed(control with { State = RemoteState.Connected }),
            "failed" or "policy_disabled" => Changed(new RemoteControl(RemoteState.Failed) { Problem = bridge.TextOr("detail", "Remote Control stopped.") }),
            _ => Changed(control with { State = RemoteState.Ready }),
        };
    }

    private Reaction Changed(RemoteControl next)
    {
        if (next == control)
        {
            return Reaction.None;
        }

        control = next;

        return Reaction.Of(new RemoteControlChanged(session, next));
    }
}
