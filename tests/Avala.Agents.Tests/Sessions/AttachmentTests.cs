using Avala.Agents.Contracts;
using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Sessions;
using Avala.Agents.Sessions;
using Avala.Agents.Tests.Connections;
using Avala.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Avala.Agents.Tests.Sessions;

public sealed class AttachmentTests
{
    private static readonly AgentRequest Request = new("/worktrees/1");

    private static readonly Attachment Screenshot = new("screen.png", "image/png", "/data/attachments/1/screen.png");

    private static readonly Attachment Log = new("build.log", "text/x-log", "/data/attachments/2/build.log");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AttachmentsOfTypesTheProviderAcceptsReachTheSessionWithTheMessageAsync()
    {
        var provider = Accepting("image/png", "text/*");
        await using var agents = Agents(provider);
        var opened = Outcomes.Succeeds(await agents.OpenAsync(Request, Cancellation));

        Outcomes.Succeeds(await agents.SayAsync(opened.Session, new UserTurn("Why does this fail?") { Attachments = [Screenshot, Log] }, Cancellation));

        Assert.Equal([Screenshot, Log], Assert.Single(Assert.Single(provider.Sessions).Turns).Attachments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("image/png")]
    public async Task AnAttachmentOfATypeTheProviderDoesNotAcceptIsRefusedWithoutAskingItAsync(string accepted)
    {
        var provider = accepted.Length == 0 ? new ScriptedAgentProvider(ScriptedAgentProvider.Reply) : Accepting(accepted);
        await using var agents = Agents(provider);
        var opened = Outcomes.Succeeds(await agents.OpenAsync(Request, Cancellation));

        Assert.Equal(AgentError.UnacceptedAttachment, Outcomes.FailsWith(await agents.SayAsync(opened.Session, new UserTurn("Read the log") { Attachments = [Screenshot, new("spec.pdf", "application/pdf", "/data/spec.pdf")] }, Cancellation)));
        Assert.Empty(Assert.Single(provider.Sessions).Turns);
    }

    private static ScriptedAgentProvider Accepting(params string[] types) =>
        new(ScriptedAgentProvider.Reply) { Capabilities = ScriptedAgentProvider.Declared.With(new AcceptsAttachments(types)) };

    private static AgentSessions Agents(params IAgentProvider[] providers) =>
        new(Connected.Starter(Connected.Registry(providers), [], []), new RecordingBus(), TimeProvider.System, NullLogger<AgentSessions>.Instance);
}
