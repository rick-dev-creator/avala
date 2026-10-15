using Avala.Sdk;

namespace Avala.Agents.Contracts.Sessions;

public enum RemoteState
{
    Off,
    Starting,
    Ready,
    Connected,
    Failed,
}

public sealed record RemoteControl(RemoteState State)
{
    public static RemoteControl Off { get; } = new(RemoteState.Off);

    public Option<string> Link { get; init; }

    public Option<string> Problem { get; init; }

    public bool IsOn => State is RemoteState.Starting or RemoteState.Ready or RemoteState.Connected;
}
