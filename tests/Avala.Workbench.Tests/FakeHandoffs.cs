using Avala.Handoffs.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Tests;

internal sealed class FakeHandoffs : IHandoffs
{
    public Dictionary<JobId, IReadOnlyList<HandoffRecord>> Records { get; } = [];

    public Dictionary<JobId, ResetWait> Waits { get; } = [];

    public IReadOnlyList<HandoffRecord> OfJob(JobId job) => Records.GetValueOrDefault(job, []);

    public Dictionary<JobId, IReadOnlyList<RelayMark>> Marks { get; } = [];

    public IReadOnlyList<RelayMark> MarksOf(JobId job) => Marks.GetValueOrDefault(job, []);

    public Option<ResetWait> WaitOf(JobId job) => Waits.TryGetValue(job, out var wait) ? wait : Option<ResetWait>.None;
}

internal sealed class FakeLimitSettings : ILimitSettings
{
    public LimitFile File { get; set; } = new(new LimitSettings(OnLimit.Hold, 0.9, 0.98, []), LimitFileStatus.Absent);

    public Option<HandoffError> Refusal { get; set; }

    public List<LimitSettings> Saved { get; } = [];

    public string MachineFile => "/data/limits.json";

    public ValueTask<LimitFile> MachineAsync(CancellationToken cancellationToken) => ValueTask.FromResult(File);

    public ValueTask<Result<LimitFile, HandoffError>> ChangeMachineAsync(LimitSettings settings, CancellationToken cancellationToken)
    {
        if (Refusal.IsSome)
        {
            return ValueTask.FromResult(Refusal.Match(Result<LimitFile, HandoffError>.Failure, () => File));
        }

        Saved.Add(settings);
        File = new LimitFile(settings, LimitFileStatus.Applied);

        return ValueTask.FromResult(Result<LimitFile, HandoffError>.Success(File));
    }
}
