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

    public const string CheckDeclaration = ".avala/checks.json";

    public const string PolicyFile = ".avala/permissions.json";

    extension(JobEvidence evidence)
    {
        public IReadOnlyList<ExceptionReason> Exceptions =>
        [
            .. evidence.Rules.Match<ExceptionReason[]>(
                rules => rules.Approve == ApprovalRule.CleanEvidence ? [] : [ExceptionReason.NotDeclared],
                () => [ExceptionReason.UnreadableRules]),
            .. evidence.RunExceptions,
        ];

        public IReadOnlyList<ExceptionReason> RunExceptions =>
        [
            .. When(!evidence.Latest.Match(report => report.Outcome == VerificationOutcome.Passed, () => false), ExceptionReason.VerificationNotPassed),
            .. When(evidence.Denials.Count + evidence.DeniedAnswers.Count + evidence.Declined.Count > 0, ExceptionReason.Denial),
            .. When(evidence.Assumed.Count > 0, ExceptionReason.Assumption),
            .. When(evidence.RuleFiles.Count > 0, ExceptionReason.RuleFileEdited),
            .. When(evidence.Holds.Count > 0, ExceptionReason.Held),
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

        public IReadOnlyList<PolicyDecision> Denials => [.. evidence.Decisions.Where(decision => decision.Answer == PolicyAnswer.Deny)];

        public IReadOnlyList<HumanAnswer> DeniedAnswers => [.. evidence.Answers.Where(answer => answer.Answer == PermissionAnswer.Deny)];

        public IReadOnlyList<FormDecision> Declined => [.. evidence.Forms.Where(form => form.Answer.Match(answer => answer.Declined, () => false))];

        public IReadOnlyList<FormDecision> Assumed => [.. evidence.Forms.Where(form => form.Assumptions.Count > 0)];

        public IReadOnlyList<AttemptRecord> Holds => [.. evidence.Attempts.Where(attempt => attempt.Origin == AttemptOrigin.Hint)];

        public IReadOnlyList<string> RuleFiles =>
        [
            .. evidence.ChangedFiles.Match(files => files.Where(file => file.StartsWith(RuleFolder, StringComparison.Ordinal)), () => []),
            .. When(evidence.Latest.Bind(report => report.Declaration).Match(origin => origin.EditedInWorktree, () => false), CheckDeclaration),
            .. When(evidence.RuleOrigins.Any(origin => origin.EditedInWorktree), PolicyFile),
        ];

        public RunEvidence Run(JobId job) => new(job, evidence.Summary, evidence.RunExceptions)
        {
            Verifications = evidence.Verifications,
            Denials = evidence.Denials,
            DeniedAnswers = evidence.DeniedAnswers,
            Declined = evidence.Declined,
            Assumed = evidence.Assumed,
            RuleFiles = [.. evidence.RuleFiles.Distinct(StringComparer.Ordinal)],
            Holds = evidence.Holds,
            AllowedByRules = evidence.Decisions.Count(decision => decision.Answer == PolicyAnswer.Allow),
        };
    }

    private static ExceptionReason[] When(bool found, ExceptionReason reason) => found ? [reason] : [];

    private static string[] When(bool found, string file) => found ? [file] : [];
}
