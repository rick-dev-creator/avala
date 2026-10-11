using System.Globalization;
using System.Text;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;
using Avala.Workspaces.Contracts;

namespace Avala.Verification.Verifying;

internal static class AgentFeedback
{
    public static GateVerdict Judge(IReadOnlyList<CheckEvidence> checks) =>
        checks.FirstOrDefault(check => check.Status is not (CheckStatus.Passed or CheckStatus.Skipped)) is { } failed
            ? GateVerdict.Retry(Describe(failed))
            : GateVerdict.Pass;

    public static GateVerdict Noting(GateVerdict verdict, Option<FileOrigin> declaration) =>
        verdict.Decision == GateDecision.Retry && declaration.Match(origin => origin.EditedInWorktree, () => false)
            ? GateVerdict.Retry(
                $"{verdict.Feedback}\n\nYour changes to {CheckDeclaration.RelativePath} do not apply to this job: its checks come from the commit it started from.")
            : verdict;

    private static string Describe(CheckEvidence check)
    {
        var feedback = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"The repository check \"{check.Name}\" did not pass: ")
            .Append(Cause(check))
            .Append(" Fix the cause, then finish the turn again.");

        Section(feedback, "Output", check.OutputTail);
        Section(feedback, "Errors", check.ErrorTail);

        return feedback.ToString();
    }

    private static string Cause(CheckEvidence check) => check.Status switch
    {
        CheckStatus.TimedOut => $"`{check.Command}` was stopped after {Seconds(check.Duration)}, its time limit.",
        CheckStatus.NotFound => $"`{check.Command}` could not start because its command was not found.",
        _ => $"`{check.Command}` exited with code {check.ExitCode.Match(code => code, () => -1).ToString(CultureInfo.InvariantCulture)} after {Seconds(check.Duration)}.",
    };

    private static string Seconds(TimeSpan duration) =>
        string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:0.0} s");

    private static void Section(StringBuilder feedback, string title, string tail)
    {
        if (tail.Length > 0)
        {
            feedback.Append("\n\n").Append(title).Append(":\n").Append(tail);
        }
    }
}
