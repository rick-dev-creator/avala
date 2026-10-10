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

    public PermissionEntry OfferingTheRepository(string target = "npm test -- CheckoutForm.test.tsx --runInBand") =>
        Permission(target, DecisionDelivery.LeftToHuman, new PolicyRule(RuleOrigin.Repository, "always in this repository", ItemKind.Command, target, RuleScope.Anywhere, PolicyAnswer.Allow));

    private PermissionEntry Permission(string target, DecisionDelivery delivery, Option<PolicyRule> repository) =>
        Assert.IsType<PermissionEntry>(Assert.Single(Transcript.Empty
            .Apply(new TurnStarted(Session, Turn), Now)
            .Apply(new PermissionRequested(Session, Turn, new ItemId("run"), "Run the CheckoutForm tests", ItemKind.Command, target), Now)
            .Apply(new PolicyDecision(Session, Turn, new ItemId("run"), Option<JobId>.None, ItemKind.Command, target, PolicyAnswer.Ask, Option<PolicyRule>.None, delivery, Now) { RepositoryRule = repository })
            .Entries));

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
