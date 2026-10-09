using Avala.Jobs.Contracts;
using Avala.Storage;

namespace Avala.Jobs.Storage;

internal sealed class StoredChoice
{
    public int Key { get; init; }

    public Guid Job { get; init; }

    public long At { get; init; }

    public string Choice { get; init; } = string.Empty;

    public static StoredChoice Of(JobId job, ConnectionChoice choice) => new()
    {
        Job = job.Value,
        At = choice.At.UtcTicks,
        Choice = StoredJson.Write(choice),
    };

    public ConnectionChoice Read() => StoredJson.Read<ConnectionChoice>(Choice);
}
