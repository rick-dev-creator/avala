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
            [new ReportedRun("dotnet test 2>&1 | tee test.log", Passed: true) { Masked = true }, new ReportedRun("node --test; echo done", Passed: true) { Masked = true }],
            ReportedRuns.AfterLastEdit(entries));
    }

    [Fact]
    public void AVerificationChainedOnlyWithAndKeepsItsResultAndOneAPipeHidesIsSaidToHaveRun()
    {
        var reported = ReportedRuns.AfterLastEdit(
        [
            Tool(ItemKind.Command, "dotnet build && dotnet test", ItemOutcome.Failed),
            Tool(ItemKind.Command, "npm test 2>&1 | tail -20", ItemOutcome.Succeeded),
        ]);

        Assert.Equal(
            ["dotnet build && dotnet test · failed", "npm test 2>&1 | tail -20 · ran, its result hidden by what followed it"],
            reported.Select(Avala.Workbench.Review.ReviewPhrases.Ran));
    }

    [Fact]
    public void AnAgentThatNeverEditedHasEveryVerificationReportedAndIsNotSaidToHaveEdited()
    {
        ITimelineEntry[] entries = [Tool(ItemKind.Command, "dotnet test", ItemOutcome.Succeeded)];

        Assert.Equal([new ReportedRun("dotnet test", Passed: true)], ReportedRuns.AfterLastEdit(entries));
        Assert.False(ReportedRuns.Edited(entries));
    }

    [Fact]
    public void AToolWithoutItsInputIsReportedByItsTitleWithoutTheWordRun() =>
        Assert.Equal(
            [new ReportedRun("make check", Passed: false)],
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
    [InlineData("git -C . checkout -- src")]
    [InlineData("git --no-pager stash pop")]
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
    [InlineData("clang-format -i src/rounding.c")]
    [InlineData("goimports -w .")]
    [InlineData("npm run format")]
    [InlineData("dotnet add package Newtonsoft.Json")]
    [InlineData("pip3 install requests")]
    [InlineData("cargo add serde")]
    [InlineData("python3 -m pip install requests")]
    [InlineData("pnpm i")]
    [InlineData("cargo update")]
    [InlineData("uv sync")]
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
    [InlineData("npm run lint:fix")]
    [InlineData("bundle exec rubocop -A")]
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

    [Theory]
    [InlineData("dotnet build && printf '}' >> src/Rounding.cs")]
    [InlineData("dotnet build && printf '}' | tee -a src/Rounding.cs")]
    public void ACommandRunBeforeAVerificationLineThatAlsoWroteASourceFileIsNotReportedAsAfterTheLastEdit(string line) =>
        Assert.DoesNotContain(
            new ReportedRun("dotnet test", Passed: true),
            ReportedRuns.AfterLastEdit(
            [
                Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded),
                Tool(ItemKind.Command, "dotnet test", ItemOutcome.Succeeded),
                Tool(ItemKind.Command, line, ItemOutcome.Succeeded),
            ]));

    [Theory]
    [InlineData("CI=true npm test > test.log")]
    [InlineData("timeout 600 dotnet test > test.log")]
    public void AVerificationRunWithAVariableOrAWrapperThatLogsToAFileIsReportedAndIsNotAnEdit(string line) =>
        Assert.Equal(
            [new ReportedRun(line, Passed: true)],
            ReportedRuns.AfterLastEdit([Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded), Tool(ItemKind.Command, line, ItemOutcome.Succeeded)]));

    [Theory]
    [InlineData("npx tsc --version")]
    [InlineData("pytest --version")]
    [InlineData("npm ls jest")]
    public void AskingForACheckersVersionOrWhetherItIsInstalledIsNotReported(string inspection) =>
        Assert.Empty(ReportedRuns.AfterLastEdit(
        [
            Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, inspection, ItemOutcome.Succeeded),
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

    [Theory]
    [InlineData("npx jest -u")]
    [InlineData("npx jest --updateSnapshot")]
    [InlineData("npx vitest run -u")]
    public void ACommandRunBeforeATestRunThatRewroteItsSnapshotsIsNotReportedAsAfterTheLastEdit(string update) =>
        Assert.Empty(ReportedRuns.AfterLastEdit(
        [
            Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, "dotnet test", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, update, ItemOutcome.Succeeded),
        ]));

    [Theory]
    [InlineData("python3 - <<'PY'\nfrom pathlib import Path\nPath('src/rounding.py').write_text('x')\nPY")]
    [InlineData("python3 -c \"open('src/rounding.py', 'w').write('x')\"")]
    [InlineData("node -e \"require('fs').writeFileSync('src/rounding.js', '')\"")]
    public void ACommandRunBeforeAnInlineScriptThatWroteASourceFileIsNotReportedAsAfterTheLastEdit(string script) =>
        Assert.Empty(ReportedRuns.AfterLastEdit(
        [
            Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, "dotnet test", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, script, ItemOutcome.Succeeded),
        ]));

    [Theory]
    [InlineData("dotnet test --list-tests")]
    [InlineData("pytest --collect-only")]
    [InlineData("npx jest --listTests")]
    public void ListingTestsWithoutRunningThemIsNotReported(string inspection) =>
        Assert.Empty(ReportedRuns.AfterLastEdit(
        [
            Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded),
            Tool(ItemKind.Command, inspection, ItemOutcome.Succeeded),
        ]));

    [Fact]
    public void AVerboseCtestRunIsReported() =>
        Assert.Equal(
            [new ReportedRun("ctest -V", Passed: true)],
            ReportedRuns.AfterLastEdit([Tool(ItemKind.FileEdit, "src/rounding.c", ItemOutcome.Succeeded), Tool(ItemKind.Command, "ctest -V", ItemOutcome.Succeeded)]));

    [Theory]
    [InlineData("dotnet test 2>&1 | tail -20")]
    [InlineData("npm test 2>&1 | head -50")]
    public void AVerificationWhoseFailureAPipeHidesIsNotReportedAsPassed(string line) =>
        Assert.DoesNotContain(
            new ReportedRun(line, Passed: true),
            ReportedRuns.AfterLastEdit([Tool(ItemKind.FileEdit, "src/Rounding.cs", ItemOutcome.Succeeded), Tool(ItemKind.Command, line, ItemOutcome.Succeeded)]));

    private static ToolEntry Tool(ItemKind kind, string input, Option<ItemOutcome> outcome) =>
        new($"item:{Guid.NewGuid():N}", kind, input, string.Empty, outcome) { Input = input };
}
