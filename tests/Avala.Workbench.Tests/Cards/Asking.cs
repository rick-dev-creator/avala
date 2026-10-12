using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Timeline;

namespace Avala.Workbench.Tests.Cards;

internal sealed class Asking
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    public SessionId Session { get; } = SessionId.New();

    public TurnId Turn { get; } = TurnId.New();

    public static FormField Choice(params FormOption[] options) =>
        new("database", "Database", "Which one?", FieldKind.SingleChoice, options);

    public static FormField[] Database { get; } =
        [Choice(new FormOption("PostgreSQL", "Relational", Recommended: true), new FormOption("SQLite", "A file"))];

    public PermissionEntry Permission(string target = "npm test -- CheckoutForm.test.tsx --runInBand", DecisionDelivery delivery = DecisionDelivery.LeftToHuman) =>
        Permission(target, delivery, Option<PolicyRule>.None);

    public PermissionEntry Scoped(string target, string programs, bool raisable = true) =>
        Permission(target, DecisionDelivery.LeftToHuman, Option<PolicyRule>.None, scope: Offered(programs, raisable));

    public PermissionEntry Refused(string target, ScopeRefusal refusal, bool raisable = true) =>
        Permission(target, DecisionDelivery.LeftToHuman, Option<PolicyRule>.None, scope: new JobScope(Option<PolicyRule>.None, refusal, raisable));

    public static JobScope Offered(string programs, bool raisable = true) =>
        new(new PolicyRule(RuleOrigin.Job, $"always {programs.Replace(" ", ", ", StringComparison.Ordinal)} in this job", ItemKind.Command, programs, RuleScope.Anywhere, PolicyAnswer.Allow) { AnyArguments = true }, Option<ScopeRefusal>.None, raisable);

    public PermissionEntry OfferingTheRepository(string target = "npm test -- CheckoutForm.test.tsx --runInBand") =>
        Permission(target, DecisionDelivery.LeftToHuman, new PolicyRule(RuleOrigin.Repository, "always in this repository", ItemKind.Command, target, RuleScope.Anywhere, PolicyAnswer.Allow));

    public PermissionEntry Contested(string target = "dotnet ef database update") =>
        Permission(target, DecisionDelivery.LeftToHuman, Option<PolicyRule>.None, contested: true);

    private PermissionEntry Permission(string target, DecisionDelivery delivery, Option<PolicyRule> repository, bool contested = false, Option<JobScope> scope = default) =>
        Assert.IsType<PermissionEntry>(Assert.Single(Transcript.Empty
            .Apply(new TurnStarted(Session, Turn), Now)
            .Apply(new PermissionRequested(Session, Turn, new ItemId("run"), "Run the CheckoutForm tests", ItemKind.Command, target), Now)
            .Apply(new PolicyDecision(Session, Turn, new ItemId("run"), Option<JobId>.None, ItemKind.Command, target, PolicyAnswer.Ask, Option<PolicyRule>.None, delivery, Now)
            {
                RepositoryRule = repository,
                Contested = contested,
                Scope = scope.IsSome ? scope : Offered(target.Split(' ')[0]),
            })
            .Entries));

    public PermissionEntry AskingParent(JobId parent, DecisionDelivery delivery, Option<PassReason> passed, Option<PermissionAnswer> resolution)
    {
        var decided = Transcript.Empty
            .Apply(new TurnStarted(Session, Turn), Now)
            .Apply(new PermissionRequested(Session, Turn, new ItemId("run"), "Migrate the database", ItemKind.Command, "dotnet ef database update"), Now)
            .Apply(new PolicyDecision(Session, Turn, new ItemId("run"), Option<JobId>.None, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, delivery, Now)
            {
                Parent = parent,
                Passed = passed,
            });
        var resolved = resolution.Match(answer => decided.Apply(new PermissionResolved(Session, Turn, new ItemId("run"), answer), Now), () => decided);

        return Assert.IsType<PermissionEntry>(Assert.Single(resolved.Entries));
    }

    public FormEntry Form(params FormField[] fields) => Form(FormPurpose.Question, fields.Length == 0 ? Database : fields);

    public FormEntry Form(FormPurpose purpose, FormField[] fields)
    {
        var form = new AgentForm(purpose, "Add invoice PDF endpoint", "The service stores invoices.", fields);

        return Assert.IsType<FormEntry>(Assert.Single(Transcript.Empty
            .Apply(new TurnStarted(Session, Turn), Now)
            .Apply(new FormRequested(Session, Turn, new ItemId("question"), form), Now)
            .Apply(new FormDecision(Session, Turn, new ItemId("question"), Option<JobId>.None, form, Autonomy.Supervised, Option<FormAnswer>.None, [], DecisionDelivery.LeftToHuman, Now))
            .Entries));
    }
}
