using Avala.Autopilot.Contracts;

namespace Avala.Workbench.Triggers;

internal interface ILoopItemViewModel
{
    LoopId Id { get; }

    string Repository { get; }

    string Status { get; }

    bool NeedsAttention { get; }

    string Summary { get; }

    IReadOnlyList<string> Iterations { get; }

    bool CanPause { get; }

    bool CanResume { get; }

    bool CanStop { get; }
}

internal sealed class LoopItemViewModel(LoopDigest digest, TimeZoneInfo zone) : ILoopItemViewModel
{
    public LoopId Id { get; } = digest.State.Loop;

    public string Repository { get; } = digest.State.Repository;

    public string Status { get; } = LoopPhrases.Status(digest, zone);

    public bool NeedsAttention { get; } = LoopPhrases.NeedsAttention(digest.State);

    public string Summary { get; } = LoopPhrases.Summary(digest);

    public IReadOnlyList<string> Iterations { get; } = LoopPhrases.Iterations(digest);

    public bool CanPause { get; } = digest.State.Status is LoopStatus.Running or LoopStatus.Waiting;

    public bool CanResume { get; } = digest.State.Status == LoopStatus.Paused;

    public bool CanStop { get; } = digest.State.Status != LoopStatus.Ended;
}
