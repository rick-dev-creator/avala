using Avala.Handoffs.Contracts;
using Avala.Handoffs.Watching;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Storage;

namespace Avala.Handoffs.Storage;

internal sealed class StoredHandoff
{
    public int Key { get; init; }

    public Guid Job { get; init; }

    public string Handoff { get; init; } = string.Empty;

    public static StoredHandoff Of(HandoffRecord handoff) => new() { Job = handoff.Job.Value, Handoff = StoredJson.Write(handoff) };

    public HandoffRecord Read() => StoredJson.Read<HandoffRecord>(Handoff);
}

internal sealed class StoredWait
{
    public Guid Job { get; init; }

    public string Pending { get; init; } = string.Empty;

    public long Since { get; init; }

    public static StoredWait Of(KeptWait wait) => new()
    {
        Job = wait.Job.Value,
        Pending = wait.Pending.Match(text => text, () => string.Empty),
        Since = wait.Since.UtcTicks,
    };

    public KeptWait Read() =>
        new(new JobId(Job), Pending.Length == 0 ? Option<string>.None : Pending, new DateTimeOffset(Since, TimeSpan.Zero));
}
