using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Events;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.Contracts;
using Avala.Workbench.Settings;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Settings;

public sealed class RuleFileViewModelScripts
{
    [Fact]
    public void ARuleFileShowsItsStatusAndTheShortCommitItWasReadFrom() =>
        ViewModelScript.Given(new RuleFileViewModel(".avala/permissions.json", "Applied", new FileOrigin("4f2c9e1d0b7a", true), "3 rules"))
            .Then(file => Assert.Equal((".avala/permissions.json", "Applied", "4f2c9e1", true, false, "3 rules"), (file.Path, file.Status, file.Commit, file.EditedInCheckout, file.IsRejected, file.Summary)));

    [Fact]
    public void AFileNotYetCommittedHasNoCommit() =>
        ViewModelScript.Given(new RuleFileViewModel(".avala/jobs.json", "Absent", Option<FileOrigin>.None, "0 sections"))
            .Then(file => Assert.Equal(("no commit", false), (file.Commit, file.EditedInCheckout)));

    [Fact]
    public void ARejectedFileIsFlaggedWithItsReason() =>
        ViewModelScript.Given(new RuleFileViewModel(".avala/checks.json", "Rejected: Malformed", new FileOrigin("70d2e11aa", false), "no checks"))
            .Then(file => Assert.Equal((true, "Rejected: Malformed"), (file.IsRejected, file.Status)));
}

public sealed class RuleViewModelScripts
{
    [Fact]
    public void ARuleShowsWhereItComesFromWhatItMatchesAndItsAnswer() =>
        ViewModelScript.Given(new RuleViewModel(new PolicyRule(RuleOrigin.Repository, "tests", ItemKind.Command, "npm test*", RuleScope.Anywhere, PolicyAnswer.Allow), 3))
            .Then(rule => Assert.Equal(("Repository", "tests", "Command", "npm test*", PolicyAnswer.Allow, 3), (rule.Origin, rule.Name, rule.Kind, rule.Target, rule.Answer, rule.Order)));

    [Fact]
    public void ARuleWithoutKindOrTargetMatchesAnything() =>
        ViewModelScript.Given(new RuleViewModel(new PolicyRule(RuleOrigin.BuiltIn, "deny the rest", Option<ItemKind>.None, Option<string>.None, RuleScope.Anywhere, PolicyAnswer.Deny), 1))
            .Then(rule => Assert.Equal(("Built-in", "any", "anything"), (rule.Origin, rule.Kind, rule.Target)));
}

public sealed class CapsViewModelScripts
{
    [Fact]
    public void CapsShowTheirScopeAndTheirLimits() =>
        ViewModelScript.Given(new CapsViewModel("claude-personal", "2 USD per job", SettingsPhrases.Lines(new Budgets.Contracts.BudgetCaps([new Cost(2m, "USD")], 400_000L, 0.9))))
            .Then(caps => Assert.Equal(("claude-personal", "2 USD per job"), (caps.Scope, caps.Caps)))
            .Then(caps => Assert.Equal([new SettingLine("Cost per job", "2 USD"), new SettingLine("Tokens per job", "400,000"), new SettingLine("Hold when a usage window reaches", "90%")], caps.Lines));

    [Fact]
    public void ABudgetWithoutCapsSaysJobsRunWithoutOne() =>
        Assert.Equal([new SettingLine("No caps", "jobs run without a budget")], SettingsPhrases.Lines(new Budgets.Contracts.BudgetCaps([], Option<long>.None, Option<double>.None)));
}

public sealed class CheckViewModelScripts
{
    [Fact]
    public void ACheckShowsItsCommandAndTimeoutInSeconds() =>
        ViewModelScript.Given(new CheckViewModel(new CheckDeclared("tests", "npm test", TimeSpan.FromMinutes(5))))
            .Then(check => Assert.Equal(("tests", "npm test", "300s"), (check.Name, check.Command, check.Timeout)));
}

public sealed class JobSectionViewModelScripts
{
    [Fact]
    public void ASectionOfTheJobFileShowsItsNameAndValueAsWritten() =>
        ViewModelScript.Given(new JobSectionViewModel("delegation", """{ "maxDepth": 2 }"""))
            .Then(section => Assert.Equal(("delegation", """{ "maxDepth": 2 }"""), (section.Name, section.Value)));
}

public sealed class MachineConnectionViewModelScripts
{
    [Fact]
    public void AConnectionShowsItsProviderAndCredentialSource() =>
        ViewModelScript.Given(new MachineConnectionViewModel(new DeclaredConnection(new ConnectionName("claude-work"), "claude-code", "keychain"), true))
            .Then(connection => Assert.Equal(("claude-work", "claude-code", "keychain", true), (connection.Name, connection.Provider, connection.Source, connection.IsDefault)));

    [Fact]
    public void AConnectionWithoutASourceUsesTheProvidersOwnLogin() =>
        ViewModelScript.Given(new MachineConnectionViewModel(new DeclaredConnection(new ConnectionName("claude-personal"), "claude-code", Option<string>.None), false))
            .Then(connection => Assert.Equal(("the provider's own login", false), (connection.Source, connection.IsDefault)));
}
