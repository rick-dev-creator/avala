using Avala.Forges.Contracts;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workbench.Timeline;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Tests.Conversation;

public sealed class CiViewModelScripts
{
    private static readonly CiEntry Failed = new(
        EntryKeys.Ci(12, "4f2a9c1e"),
        12,
        "4f2a9c1e",
        [
            new CheckRun("build (ubuntu-latest)", CheckStatus.Passed, Option<Uri>.None, string.Empty),
            new CheckRun("build (windows-latest)", CheckStatus.Failed, new Uri("https://ci.example/runs/7"), "2 tests failed"),
        ]);

    [Fact]
    public void AFailedCheckBecomesACardNamingItThatAsksTheAgentToFixIt()
    {
        var asked = new List<string?>();

        ViewModelScript.Given(new CiViewModel(Failed, FakeLinks.Opening))
            .When(ci => ci.Mark(true, new AsyncRelayCommand<string?>(text =>
            {
                asked.Add(text);

                return Task.CompletedTask;
            })))
            .When(ci => ci.AskCommand.Execute(ci.Request))
            .Then(ci => Assert.Equal(("CI failed: build (windows-latest)", true, true), (ci.Headline, ci.IsFailed, ci.CanAsk)))
            .Then(ci => Assert.Equal(["build (windows-latest): 2 tests failed"], ci.Failing.Select(check => $"{check.Name}: {check.Summary}")))
            .Then(_ => Assert.Equal(
                ["The checks of pull request #12 failed on 4f2a9c1:\n- build (windows-latest): 2 tests failed (https://ci.example/runs/7)\nFix what makes them fail."],
                asked));
    }

    [Fact]
    public void ACardWhoseFailureWokeTheAgentSaysSoInsteadOfOfferingToAsk() =>
        ViewModelScript.Given(new CiViewModel(Failed with { Woken = true }, FakeLinks.Opening))
            .When(ci => ci.Mark(true, new AsyncRelayCommand<string?>(_ => Task.CompletedTask)))
            .Then(ci => Assert.Equal(("Avala woke the agent to fix them", false), (ci.Note, ci.CanAsk)));

    [Fact]
    public void AJobThatTakesNoMessageIsNotOfferedTheAsk() =>
        ViewModelScript.Given(new CiViewModel(Failed, FakeLinks.Opening))
            .When(ci => ci.Mark(false, new AsyncRelayCommand<string?>(_ => Task.CompletedTask)))
            .Then(ci => Assert.False(ci.CanAsk));

    [Fact]
    public void ChecksThatAllPassedBecomeOneQuietLine() =>
        ViewModelScript.Given(new CiViewModel(Failed with { Checks = [new CheckRun("build", CheckStatus.Passed, Option<Uri>.None, string.Empty), new CheckRun("lint", CheckStatus.Passed, Option<Uri>.None, string.Empty)] }, FakeLinks.Opening))
            .Then(ci => Assert.Equal(("All 2 checks of pull request #12 passed", false, false), (ci.Headline, ci.IsFailed, ci.CanAsk)));

    [Fact]
    public async Task AFailedChecksLogOpensFromItsCardAsync()
    {
        var opener = new FakeLinks();
        var ci = new CiViewModel(Failed, new Avala.Workbench.Linking.Links(opener, new CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger()));

        await Assert.Single(ci.Failing).OpenLogCommand.ExecuteAsync(null);

        Assert.Equal([new Uri("https://ci.example/runs/7")], opener.Opened);
    }
}
