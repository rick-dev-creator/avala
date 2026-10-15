using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Workbench.Board;

namespace Avala.Workbench.Remoting;

internal static class RemotePhrases
{
    public const string Off = "Follow and continue this job from the Claude app or any browser.";

    public const string Starting = "Starting Remote Control…";

    public const string Waiting = "Open the link or scan the code with your phone.";

    public const string Connected = "Your phone is connected.";

    public const string Paused = "Paused while the job is held. It comes back when the job runs.";

    public const string Stopped = "Remote Control stopped.";

    public const string Trust = "Avala's permission rules, budgets and review still decide. Answers from your phone are recorded in the audit.";

    public static bool Offered(BoardJob job, bool controllable) =>
        controllable
        && job.Summary.Parent.IsNone
        && job.Status is not (JobStatus.Draft or JobStatus.Approved or JobStatus.Discarded or JobStatus.Failed);

    public static string Status(bool wanted, JobStatus status, RemoteControl remote) =>
        !wanted ? Off
        : status == JobStatus.NeedsHelp ? Paused
        : remote.State switch
        {
            RemoteState.Connected => Connected,
            RemoteState.Ready => Waiting,
            _ => Starting,
        };

    public static string Refusal(JobRejection rejection) => rejection switch
    {
        JobRejection.NotRoot => "A delegated job follows its orchestrator's session.",
        JobRejection.NotRemoteControllable => "This connection cannot be continued from a phone.",
        _ => "Remote Control could not be changed for this job.",
    };
}
