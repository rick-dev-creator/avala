using Avala.Agents.Contracts.Connections;
using Avala.Jobs.Contracts;
using Avala.Sdk;

namespace Avala.Workbench.Submitting;

internal static class Resubmission
{
    public static bool IsOffered(JobSummary held, Option<HoldReason> hold) =>
        held.Status == JobStatus.NeedsHelp && hold == Option<HoldReason>.Some(HoldReason.InvalidChecks) && held.Parent.IsNone;

    public static JobRequest Of(JobSummary held, Option<ConnectionChoice> choice) =>
        new(held.Repository, held.Instruction)
        {
            Connection = choice.IsSome ? Option<ConnectionName>.None : held.Connection,
            Autonomy = held.Autonomy,
            Model = held.Model,
        };
}
