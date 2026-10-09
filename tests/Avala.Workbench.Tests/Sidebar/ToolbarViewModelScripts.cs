using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Sdk.Presentation;
using Avala.Testing;
using Avala.Workbench.Sidebar;
using Avala.Workbench.Tests.Cards;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.Messaging;

namespace Avala.Workbench.Tests.Sidebar;

public sealed class ToolbarViewModelScripts : IDisposable
{
    private readonly Bench bench = new();

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void TheBadgeCountsEveryDecisionWaitingAcrossJobs()
    {
        var asking = new Asking();
        var running = Bench.OnBoard(bench.Job("Fix flaky CheckoutForm test", JobStatus.Running));
        var quiet = Bench.OnBoard(bench.Job("Fix JPY rounding in invoice totals", JobStatus.Running));

        ViewModelScript.Given(bench.Toolbar())
            .When(toolbar => toolbar.Show(Bench.Of(running with { Transcript = Waiting(asking) }, quiet)))
            .ThenNotified(nameof(ToolbarViewModel.PendingDecisions), nameof(ToolbarViewModel.HasPendingDecisions))
            .Then(toolbar => Assert.Equal((1, true), (toolbar.PendingDecisions, toolbar.HasPendingDecisions)))
            .When(toolbar => toolbar.Show(Bench.Of(running, quiet)))
            .Then(toolbar => Assert.Equal((0, false), (toolbar.PendingDecisions, toolbar.HasPendingDecisions)));
    }

    [Fact]
    public void TogglingDecisionsOpensAndClosesThePopover() =>
        ViewModelScript.Given(bench.Toolbar())
            .Invoke(nameof(ToolbarViewModel.ToggleDecisionsCommand))
            .Then(toolbar => Assert.True(toolbar.IsDecisionsOpen))
            .Invoke(nameof(ToolbarViewModel.ToggleDecisionsCommand))
            .ThenNotified(nameof(ToolbarViewModel.IsDecisionsOpen))
            .Then(toolbar => Assert.False(toolbar.IsDecisionsOpen));

    [Fact]
    public void ClosingThePopoverWhenItIsClosedChangesNothing() =>
        ViewModelScript.Given(bench.Toolbar())
            .Invoke(nameof(ToolbarViewModel.CloseDecisionsCommand))
            .Then(toolbar => Assert.False(toolbar.IsDecisionsOpen))
            .Invoke(nameof(ToolbarViewModel.ToggleDecisionsCommand))
            .Invoke(nameof(ToolbarViewModel.CloseDecisionsCommand))
            .Then(toolbar => Assert.False(toolbar.IsDecisionsOpen));

    [Fact]
    public void NewJobAsksTheShellForTheNewJobPageAndClosesThePopover()
    {
        var requested = new List<IPage>();
        bench.Messenger.Register<List<IPage>, PageRequested>(requested, (list, message) => list.Add(message.Page));

        ViewModelScript.Given(bench.Toolbar())
            .Invoke(nameof(ToolbarViewModel.ToggleDecisionsCommand))
            .Invoke(nameof(ToolbarViewModel.NewJobCommand))
            .Then(toolbar =>
            {
                Assert.Equal([bench.NewJob], requested);
                Assert.False(toolbar.IsDecisionsOpen);
            });
    }

    [Fact]
    public async Task ActivatingTheToolbarActivatesItsDecisionsPopoverAndFollowsTheBoardAsync()
    {
        using var toolbar = bench.Toolbar();
        var job = bench.Job("Fix flaky CheckoutForm test", JobStatus.Running);
        bench.Publish(Bench.OnBoard(job) with { Transcript = Waiting(new Asking()) });
        var decisions = Assert.IsAssignableFrom<IPresentation>(toolbar.Decisions);

        await decisions.PresentsAfterAsync(() => bench.Post(toolbar.Activate), () => "the popover did not show", Cancellation);
        await toolbar.PresentsAfterAsync(() => bench.Publish(Bench.OnBoard(job) with { Transcript = Waiting(new Asking()) }), () => $"{toolbar.PendingDecisions} pending", Cancellation);

        Assert.Equal((1, 1), await bench.Ui.ReadAsync(() => (toolbar.Decisions.Items.Count, toolbar.PendingDecisions)));
        await bench.Ui.InvokeAsync(toolbar.Deactivate, Cancellation);
    }

    public void Dispose() => bench.Dispose();

    private static Transcript Waiting(Asking asking) =>
        Transcript.Empty
            .Apply(new TurnStarted(asking.Session, asking.Turn), DateTimeOffset.UnixEpoch)
            .Apply(new PermissionRequested(asking.Session, asking.Turn, new ItemId("migrate"), "Run a command", ItemKind.Command, "dotnet ef database update"), DateTimeOffset.UnixEpoch)
            .Apply(new PolicyDecision(asking.Session, asking.Turn, new ItemId("migrate"), Option<JobId>.None, ItemKind.Command, "dotnet ef database update", PolicyAnswer.Ask, Option<PolicyRule>.None, DecisionDelivery.LeftToHuman, DateTimeOffset.UnixEpoch));
}
