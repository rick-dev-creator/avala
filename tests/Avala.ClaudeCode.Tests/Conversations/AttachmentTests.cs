using Avala.Agents.Contracts.Events;
using Avala.Agents.Contracts.Sessions;
using Avala.ClaudeCode.Conversations;
using Avala.Testing;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class AttachmentTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AnImageAPdfAndATextFileAreSentAsTheBlocksClaudeCodeReadsAsync()
    {
        using var folder = new TemporaryFolder();
        var image = await WriteAsync(folder, "screen.png", [137, 80, 78, 71]);
        var pdf = await WriteAsync(folder, "spec.pdf", [37, 80, 68, 70]);
        var note = Path.Combine(folder.Path, "log.txt");
        await File.WriteAllTextAsync(note, "Error: refund rounding", Cancellation);
        Attachment[] attachments = [new("screen.png", "image/png", image), new("spec.pdf", "application/pdf", pdf), new("log.txt", "text/plain", note)];
        var talk = new Talk();

        var blocks = await AttachmentBlocks.ReadAsync(attachments, Cancellation);
        talk.Take(talk.Conversation.Begin(new UserTurn("Why does this fail?") { Attachments = attachments }, blocks).Match(begun => begun.Reaction, _ => Reaction.None));

        var content = talk.Sent.Single()["message"]!["content"]!.AsArray();
        Assert.Equal(["text", "image", "document", "text"], content.Select(block => (string?)block!["type"]));
        Assert.Equal("Why does this fail?", (string?)content[0]!["text"]);
        Assert.Equal(("base64", "image/png", Convert.ToBase64String([137, 80, 78, 71])), ((string?)content[1]!["source"]!["type"], (string?)content[1]!["source"]!["media_type"], (string?)content[1]!["source"]!["data"]));
        Assert.Equal(("application/pdf", "spec.pdf"), ((string?)content[2]!["source"]!["media_type"], (string?)content[2]!["title"]));
        Assert.Equal("<attachment name=\"log.txt\">\nError: refund rounding\n</attachment>", (string?)content[3]!["text"]);
    }

    [Fact]
    public void AMessageWithoutAttachmentsStaysPlainText()
    {
        var talk = new Talk().Begin("Add refunds");

        Assert.Equal("Add refunds", (string?)talk.Sent.Single()["message"]!["content"]);
    }

    [Fact]
    public async Task AttachmentsSentDuringATurnJoinItWithTheirNamesAsync()
    {
        using var folder = new TemporaryFolder();
        var image = await WriteAsync(folder, "screen.png", [137, 80, 78, 71]);
        Attachment[] attachments = [new("screen.png", "image/png", image)];
        var talk = new Talk().Begin("Add refunds").Receive(Cli.Init());
        talk.Drain();

        talk.Take(talk.Conversation.Begin(new UserTurn("Like this one") { MidTurn = true, Attachments = attachments }, await AttachmentBlocks.ReadAsync(attachments, Cancellation)).Match(begun => begun.Reaction, _ => Reaction.None));

        Assert.Equal(attachments, Assert.IsType<MessageQueued>(Assert.Single(talk.Events)).Attachments);
    }

    private static async Task<string> WriteAsync(TemporaryFolder folder, string name, byte[] content)
    {
        var path = Path.Combine(folder.Path, name);
        await File.WriteAllBytesAsync(path, content, Cancellation);

        return path;
    }
}
