using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Inspector;

namespace Avala.Workbench.Tests.Inspector;

public sealed class AuditSectionViewModelScripts : IDisposable
{
    private readonly Bench bench = new();

    [Fact]
    public async Task AJobInFocusCountsWhatTheRulesAllowedAndListsEachDecision()
    {
        using var section = new AuditSectionViewModel(bench.Inspected());

        await section.FocusAsync(InspectedJobs.Reviewed(bench).Job, bench);

        Assert.Equal("1 allowed by rules · 0 answered by you · 0 denied · 0 assumptions", section.Summary);
        Assert.Equal(["Allowed Command npm test · rule tests"], section.Decisions);
        Assert.Empty(section.Assumptions);
    }

    [Fact]
    public async Task APersonsDenialCountsAsDeniedAndSaysWhoAnswered()
    {
        var job = bench.Job("Fix flaky CheckoutForm test", JobStatus.Running);
        bench.Audit.Answers.Add(new HumanAnswer(SessionId.New(), job.Job, new ItemId("run"), ItemKind.Command, "rm -rf node_modules", PermissionAnswer.Deny, Option<string>.None, Option<PolicyRule>.None, DateTimeOffset.UnixEpoch));
        using var section = new AuditSectionViewModel(bench.Inspected());

        await section.FocusAsync(job.Job, bench);

        Assert.Equal("0 allowed by rules · 1 answered by you · 1 denied · 0 assumptions", section.Summary);
        Assert.Equal(["You denied Command rm -rf node_modules"], section.Decisions);
    }

    [Fact]
    public async Task WithNothingInFocusTheSectionIsEmpty()
    {
        using var section = new AuditSectionViewModel(bench.Inspected());

        await section.FocusAsync(Option<JobId>.None, bench);

        Assert.Equal((false, string.Empty, 0), (section.IsLoaded, section.Summary, section.Decisions.Count));
    }

    public void Dispose() => bench.Dispose();
}
