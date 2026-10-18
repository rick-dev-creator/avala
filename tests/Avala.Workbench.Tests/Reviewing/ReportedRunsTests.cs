using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Delegation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using Avala.Workbench.Reviewing;
using Avala.Workbench.Timeline;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Reviewing;

public sealed class ReportedRunsTests
{
    [Fact]
    public void OnlyTheCommandsTheAgentFinishedAfterItsLastEditAreReportedOnceEachWithTheirLastOutcome()
    {
        ITimelineEntry[] entries =
        [
            Tool(ItemKind.Command, "cat src/Rounding.cs", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, "dotnet build", ItemOutcome.Failed),
            Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, "dotnet build", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, "dotnet test", ItemOutcome.Failed),
            Tool(ItemKind.Command, "rm -rf bin", ItemOutcome.Succeeded) with { Denied = true },
            Tool(ItemKind.FileEdit, "/tmp/notes.md", ItemOutcome.Failed) with { Denied = true },
            Tool(ItemKind.Command, "dotnet test", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, "dotnet watch", ItemOutcome.Cancelled),
            Tool(ItemKind.Command, "git status", Option<ItemOutcome>.None),
            Tool(ItemKind.Search, "Rounding", ItemOutcome.Succeeded),
        ];

        Assert.Equal([new ReportedRun("dotnet build", Passed: true), new ReportedRun("dotnet test", Passed: true)], ReportedRuns.AfterLastEdit(entries));
    }

    [Fact]
    public void OnlyCommandsThatLookLikeVerificationAreReportedAndOneThatWritesItsLogIsNotAnEdit()
    {
        ITimelineEntry[] entries =
        [
            Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, "git status --short", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, "grep -n test src/Rounding.cs", ItemOutcome.Failed),
            Tool(ItemKind.Command, "cat src/Rounding.cs", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, "dotnet test 2>&1 | tee test.log", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, "node --test; echo done", ItemOutcome.Succeeded),
        ];

        Assert.Equal(
            [new ReportedRun("dotnet test 2>&1 | tee test.log", Passed: true), new ReportedRun("node --test; echo done", Passed: true)],
            ReportedRuns.AfterLastEdit(entries));
    }

    [Fact]
    public void AnAgentThatNeverEditedHasEveryVerificationReportedAndIsNotSaidToHaveEdited()
    {
        ITimelineEntry[] entries = [Tool(ItemKind.Command, "dotnet test", ItemOutcome.Succeeded)];

        Assert.Equal([new ReportedRun("dotnet test", Passed: true)], ReportedRuns.AfterLastEdit(entries));
        Assert.False(ReportedRuns.Edited(entries));
    }

    [Fact]
    public void AToolWithoutItsInputIsReportedByItsTitle() =>
        Assert.Equal(
            [new ReportedRun("Run make check", Passed: false)],
            ReportedRuns.AfterLastEdit([Tool(ItemKind.Command, "make check", ItemOutcome.Failed) with { Input = Option<string>.None, Title = "Run make check" }]));

    [Fact]
    public void ACommandRunBeforeASubAgentOrAWorkerChildChangedTheFilesIsNotReportedAsAfterTheLastEdit()
    {
        var child = JobId.New();
        var integrated = new DelegationRecord(new SessionId(Guid.CreateVersion7()), new ItemId("delegate"), "Round half to even", DateTimeOffset.UnixEpoch)
        {
            Child = child,
            Role = ChildRole.Worker,
            Report = new ChildReport(child, ChildOutcome.Integrated, JobStatus.Approved, DateTimeOffset.UnixEpoch) { Files = [new FileChange("src/Rounding.cs", ChangeKind.Modified, 3, 1)] },
        };
        ITimelineEntry[] edited = [Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded), Tool(ItemKind.Command, "dotnet test", ItemOutcome.Succeeded)];

        Assert.Equal(
            (0, 0),
            (
                ReportedRuns.AfterLastEdit([.. edited, Tool(ItemKind.Subagent, "Round half to even", ItemOutcome.Succeeded) with { Steps = 1, Trail = ["Edit src/Rounding.cs"] }]).Count,
                ReportedRuns.AfterLastEdit([.. edited, Tool(ItemKind.Other, "delegate", ItemOutcome.Succeeded) with { Delegation = integrated }]).Count));
    }

    [Fact]
    public void ACommandRunBeforeACommandThatWroteAFileIsNotReportedAsAfterTheLastEdit() =>
        Assert.DoesNotContain(
            new ReportedRun("dotnet test", Passed: true),
            ReportedRuns.AfterLastEdit(
            [
                Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded),
                Tool(ItemKind.Command, "dotnet test", ItemOutcome.Succeeded),
                Tool(ItemKind.Command, "printf 'Round' > src/Rounding.cs", ItemOutcome.Succeeded),
            ]));

    [Theory]
    [InlineData("rm -rf tests")]
    [InlineData("mkdir build")]
    [InlineData("touch test")]
    [InlineData("cp -r build dist")]
    public void ACommandThatDeletesOrCreatesAFolderNamedLikeAVerificationIsAnEditAndNotAReportedRun(string command) =>
        Assert.Empty(ReportedRuns.AfterLastEdit(
        [
            Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, "dotnet test", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, command, ItemOutcome.Succeeded),
        ]));

    [Theory]
    [InlineData("git revert --no-edit HEAD")]
    [InlineData("git am fix.patch")]
    [InlineData("git clean -fd")]
    public void ACommandRunBeforeAGitCommandThatChangedTheWorkingTreeIsNotReportedAsAfterTheLastEdit(string command) =>
        Assert.Empty(ReportedRuns.AfterLastEdit(
        [
            Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, "dotnet test", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, command, ItemOutcome.Succeeded),
        ]));

    [Theory]
    [InlineData("dotnet format")]
    [InlineData("npx prettier --write .")]
    [InlineData("cargo fmt")]
    [InlineData("npx eslint --fix src")]
    [InlineData("npm install")]
    [InlineData("go generate ./...")]
    public void ACommandRunBeforeAFormatterRewroteTheFilesIsNotReportedAsAfterTheLastEdit(string formatter) =>
        Assert.Empty(ReportedRuns.AfterLastEdit(
        [
            Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, "dotnet test", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, formatter, ItemOutcome.Succeeded),
        ]));

    [Theory]
    [InlineData("npm run lint -- --fix")]
    [InlineData("ruff check --fix .")]
    [InlineData("cargo clippy --fix --allow-dirty")]
    public void ACommandRunBeforeALinterThatFixedTheFilesIsNotReportedAsAfterTheLastEdit(string linter) =>
        Assert.Empty(ReportedRuns.AfterLastEdit(
        [
            Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, "dotnet test", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, linter, ItemOutcome.Succeeded),
        ]));

    [Theory]
    [InlineData("perl -pi -e 's/Floor/Round/' src/Rounding.cs")]
    [InlineData("sed -Ei 's/Floor/Round/' src/Rounding.cs")]
    public void ACommandRunBeforeAnInPlaceRewriteWithCombinedFlagsIsNotReportedAsAfterTheLastEdit(string rewrite) =>
        Assert.Empty(ReportedRuns.AfterLastEdit(
        [
            Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, "dotnet test", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, rewrite, ItemOutcome.Succeeded),
        ]));

    [Fact]
    public void ACommandRunBeforeAVerificationLineThatAlsoWroteASourceFileIsNotReportedAsAfterTheLastEdit() =>
        Assert.DoesNotContain(
            new ReportedRun("dotnet test", Passed: true),
            ReportedRuns.AfterLastEdit(
            [
                Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded),
                Tool(ItemKind.Command, "dotnet test", ItemOutcome.Succeeded),
                Tool(ItemKind.Command, "dotnet build && printf '}' >> src/Rounding.cs", ItemOutcome.Succeeded),
            ]));

    [Theory]
    [InlineData("du -sh build")]
    [InlineData("fd test src")]
    public void AnInspectionThatNamesABuildOrTestFolderIsNotReported(string inspection) =>
        Assert.Empty(ReportedRuns.AfterLastEdit(
        [
            Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, inspection, ItemOutcome.Succeeded),
        ]));

    private static ToolEntry Tool(ItemKind kind, string input, Option<ItemOutcome> outcome) =>
        new($"item:{Guid.NewGuid():N}", kind, input, string.Empty, outcome) { Input = input };
}
