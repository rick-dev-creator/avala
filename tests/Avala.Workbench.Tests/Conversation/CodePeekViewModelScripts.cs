using System.Text;

namespace Avala.Workbench.Tests.Conversation;

public sealed class CodePeekViewModelScripts
{
    private const string Money = "export function toMinor(amount: number) {\n  return Math.round(amount * 100)\n}\n";

    [Fact]
    public async Task AFileReferenceOpensTheFileAtItsLineInAPeekAsync()
    {
        var worktree = new Worktree();
        var message = worktree.Message("Fixed in `src/money.ts:2`.");
        worktree.Files.Stored[Path.Combine(Worktree.Folder, "src/money.ts")] = Encoding.UTF8.GetBytes(Money);

        await message.OpenLinkCommand.ExecuteAsync("avala-ref:file/src%2Fmoney.ts#L2");

        Assert.Equal((true, "src/money.ts", 2, Money, "ts"), (message.Peek.IsOpen, message.Peek.Path, message.Peek.Line, message.Peek.Code, message.Peek.Language));
        Assert.Equal(string.Empty, message.LinkNotice);
    }

    [Fact]
    public async Task AFileThatIsNotInTheWorktreeSaysSoAsync()
    {
        var worktree = new Worktree();
        var message = worktree.Message("See src/gone.ts.");

        await message.OpenLinkCommand.ExecuteAsync("avala-ref:file/src%2Fgone.ts");

        Assert.Equal((false, "src/gone.ts is not in this job's worktree."), (message.Peek.IsOpen, message.LinkNotice));
    }

    [Fact]
    public async Task ABinaryFileIsNotPeekedAsync()
    {
        var worktree = new Worktree();
        var message = worktree.Message("See logo.png.");
        worktree.Files.Stored[Path.Combine(Worktree.Folder, "logo.png")] = [137, 80, 78, 71, 0, 1];

        await message.OpenLinkCommand.ExecuteAsync("avala-ref:file/logo.png");

        Assert.Equal((false, "logo.png is not a text file."), (message.Peek.IsOpen, message.LinkNotice));
    }

    [Fact]
    public async Task ClosingThePeekLetsItGoAsync()
    {
        var worktree = new Worktree();
        var message = worktree.Message("Fixed in `src/money.ts:2`.");
        worktree.Files.Stored[Path.Combine(Worktree.Folder, "src/money.ts")] = Encoding.UTF8.GetBytes(Money);
        await message.OpenLinkCommand.ExecuteAsync("avala-ref:file/src%2Fmoney.ts#L2");

        await message.Peek.CloseCommand.ExecuteAsync(null);

        Assert.Equal((false, string.Empty), (message.Peek.IsOpen, message.Peek.Code));
    }
}
