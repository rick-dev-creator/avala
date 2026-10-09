using System.Globalization;
using System.Text;
using Avala.Jobs.Contracts;
using Avala.Verification.Checks;
using Avala.Verification.Contracts;

namespace Avala.Verification.Verifying;

internal static class AgentFeedback
{
    public static GateVerdict Judge(IReadOnlyList<CheckEvidence> checks) =>
        checks.FirstOrDefault(check => check.Status is not (CheckStatus.Passed or CheckStatus.Skipped)) is { } failed
            ? GateVerdict.Retry(Describe(failed))
            : GateVerdict.Pass;

    public static GateVerdict Invalid(VerificationError error) =>
        GateVerdict.Retry(
            $"The check declaration in {CheckDeclaration.RelativePath} is invalid: {Problem(error)}. "
            + "Fix the file so its checks can run, then finish the turn again.");

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

    private static string Problem(VerificationError error) => error switch
    {
        VerificationError.MissingCommand => "a check has no command",
        VerificationError.InvalidTimeout => "a check has a timeout that is not a number of seconds between 0 and 86400",
        _ => "it is not a JSON object with a \"checks\" array of check objects",
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
