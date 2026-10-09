using System.Collections.Immutable;
using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Recording.Recordings;

internal sealed record RecordingHeader(
    DateTimeOffset RecordedAt,
    ProviderInfo Provider,
    AgentCapabilities Capabilities,
    Option<AgentAccount> Account,
    SessionOptions Options);

internal sealed record RecordedEntry(TimeSpan At, IRecordedFact Fact)
{
    public Option<AgentError> Refusal { get; init; }
}

internal sealed record SessionRecording(RecordingHeader Header, Redaction Redaction, ImmutableList<RecordedEntry> Entries)
{
    public static SessionRecording Begin(RecordingHeader header, RecordingSettings settings) =>
        new(header, new Redaction(header.Options.WorkingDirectory, settings.Redactions), []);

    public bool Ended => Entries is [.., { Fact: Stopped or StreamEnded }];

    public SessionRecording Add(TimeSpan at, IRecordedFact fact) =>
        Ended ? this
        : fact is Refused refused ? this with { Entries = Refuse(refused) }
        : this with { Entries = Entries.Add(new RecordedEntry(at, fact)) };

    public static bool Settles(IRecordedFact fact) => fact is Stopped or StreamEnded or Observed { Event: TurnCompleted };

    private ImmutableList<RecordedEntry> Refuse(Refused refused)
    {
        var input = Entries.FindLastIndex(entry => entry.Fact is IHarnessInput { } given && given.Sequence == refused.Sequence);

        return input < 0 ? Entries : Entries.SetItem(input, Entries[input] with { Refusal = refused.Error });
    }
}
