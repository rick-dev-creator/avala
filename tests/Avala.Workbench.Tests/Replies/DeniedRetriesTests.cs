using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Sdk;
using Avala.Workbench.Replies;

namespace Avala.Workbench.Tests.Replies;

public sealed class DeniedRetriesTests
{
    [Theory]
    [InlineData(ItemKind.Command, "dotnet test", "running `dotnet test`")]
    [InlineData(ItemKind.FileEdit, "NOTES.md", "editing NOTES.md")]
    [InlineData(ItemKind.Mcp, "github.create_issue", "the tool github.create_issue")]
    [InlineData(ItemKind.Search, "TODO", "TODO")]
    public void TheAgentIsToldWhatItMayNowRetry(ItemKind kind, string target, string allowed)
    {
        var answer = new HumanAnswer(SessionId.New(), JobId.New(), new ItemId("denied"), kind, target, PermissionAnswer.Allow, Option<string>.None, Option<PolicyRule>.None, DateTimeOffset.UnixEpoch);

        Assert.Equal($"Avala now allows {allowed} in this job. You may retry it.", DeniedRetries.Told(answer));
    }
}
