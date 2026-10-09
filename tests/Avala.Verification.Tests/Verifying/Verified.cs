using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Verification.Evidence;
using Avala.Verification.Verifying;
using Microsoft.Extensions.Time.Testing;

namespace Avala.Verification.Tests.Verifying;

internal sealed class Verified
{
    public const string Worktree = "/worktrees/job";

    private readonly ChecksGate gate;

    public Verified(Option<string> declaration)
    {
        Processes = new ScriptedProcesses(Clock);
        gate = new ChecksGate(
            new FixedDeclarations(declaration),
            new CheckRunner(Processes, Clock),
            new EvidenceLedger(Book, Bus),
            Clock);
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero));

    public ScriptedProcesses Processes { get; }

    public EvidenceBook Book { get; } = new();

    public RecordingBus Bus { get; } = new();

    public IReadOnlyList<VerificationReport> Published => [.. Bus.Published.OfType<AttemptVerified>().Select(verified => verified.Report)];

    public ValueTask<GateVerdict> EvaluateAsync(JobId job, int attempt, CancellationToken cancellationToken) =>
        gate.EvaluateAsync(new CompletedAttempt(job, attempt, Worktree, "Fix the calculator"), cancellationToken);

    private sealed class FixedDeclarations(Option<string> declaration) : ICheckDeclarations
    {
        public Task<Option<string>> ReadAsync(string workingDirectory, CancellationToken cancellationToken) =>
            Task.FromResult(workingDirectory == Worktree ? declaration : Option<string>.None);
    }
}
