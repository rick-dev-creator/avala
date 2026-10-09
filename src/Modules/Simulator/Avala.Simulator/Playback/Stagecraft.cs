using Avala.Agents.Contracts.Sessions;

namespace Avala.Simulator.Playback;

internal sealed record Stagecraft(IFileWriter Files, Pacing Pacing, ScenarioLibrary Library, IWorkloads Workloads);

internal sealed record Gates(ReplyGate<PermissionDecision> Permissions, ReplyGate<FormAnswer> Forms);
