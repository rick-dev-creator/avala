using Avala.Agents.Contracts.Events;
using Avala.Autopilot.Contracts;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Verification.Contracts;

namespace Avala.Autopilot.Evidence;

internal static class CleanEvidence
{
    public const string RuleFolder = ".avala/";

    extension(JobEvidence evidence)
    {
        public IReadOnlyList<ExceptionReason> Exceptions =>
        [
            .. evidence.Rules.Match<ExceptionReason[]>(
                rules => rules.Approve == ApprovalRule.CleanEvidence ? [] : [ExceptionReason.NotDeclared],
                () => [ExceptionReason.UnreadableRules]),
            .. When(!evidence.Latest.Match(report => report.Outcome == VerificationOutcome.Passed, () => false), ExceptionReason.VerificationNotPassed),
            .. When(evidence.Denied, ExceptionReason.Denial),
            .. When(evidence.Forms.Any(form => form.Assumptions.Count > 0), ExceptionReason.Assumption),
            .. When(evidence.EditedARuleFile, ExceptionReason.RuleFileEdited),
            .. When(evidence.Attempts.Any(attempt => attempt.Origin == AttemptOrigin.Hint), ExceptionReason.Held),
            .. When(evidence.ChangedFiles.IsNone, ExceptionReason.ChangesUnknown),
        ];

        public EvidenceSummary Summary => new(
            evidence.Attempts.Count,
            evidence.Latest.Map(report => report.Outcome),
            evidence.Latest.Match<IReadOnlyList<string>>(
                report => [.. report.Checks.Where(check => check.Status == CheckStatus.Passed).Select(check => check.Name)],
                () => []),
            evidence.Decisions.Count(decision => decision.Answer == PolicyAnswer.Allow) + evidence.Answers.Count(answer => answer.Answer == PermissionAnswer.Allow),
            evidence.Forms.Count,
            evidence.ChangedFiles.Match(files => files, () => []));

        public bool ChangedNothing => evidence.ChangedFiles.Match(files => files.Count == 0, () => false);

        private bool Denied =>
            evidence.Decisions.Any(decision => decision.Answer == PolicyAnswer.Deny)
            || evidence.Answers.Any(answer => answer.Answer == PermissionAnswer.Deny)
            || evidence.Forms.Any(form => form.Answer.Match(answer => answer.Declined, () => false));

        private bool EditedARuleFile =>
            evidence.ChangedFiles.Match(files => files.Any(file => file.StartsWith(RuleFolder, StringComparison.Ordinal)), () => false)
            || evidence.RuleOrigins.Any(origin => origin.EditedInWorktree)
            || evidence.Latest.Bind(report => report.Declaration).Match(origin => origin.EditedInWorktree, () => false);
    }

    private static ExceptionReason[] When(bool found, ExceptionReason reason) => found ? [reason] : [];
}
