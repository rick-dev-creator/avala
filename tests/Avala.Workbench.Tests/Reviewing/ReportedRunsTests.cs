using Avala.Agents.Contracts.Events;
using Avala.Sdk;
using Avala.Workbench.Reviewing;
using Avala.Workbench.Timeline;

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
    public void AToolWithoutItsInputIsReportedByItsTitle() =>
        Assert.Equal(
            [new ReportedRun("Run make check", Passed: false)],
            ReportedRuns.AfterLastEdit([Tool(ItemKind.Command, "make check", ItemOutcome.Failed) with { Input = Option<string>.None, Title = "Run make check" }]));

    private static ToolEntry Tool(ItemKind kind, string input, Option<ItemOutcome> outcome) =>
        new($"item:{Guid.NewGuid():N}", kind, input, string.Empty, outcome) { Input = input };
}
