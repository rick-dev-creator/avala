using Avala.Testing;
using Avala.Workbench.Conversation;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Conversation;

public sealed class EditDiffViewModelScripts
{
    [Fact]
    public void AReplacementShowsWhatItRemovedAndWhatItAddedUnderItsFile() =>
        ViewModelScript.Given(EditDiffViewModel.Of("src/money.ts", "- return units * 100\n+ return units * pow10(exponent)"))
            .Then(edit =>
            {
                Assert.Equal("src/money.ts  +1 -1", edit.Header);
                Assert.Equal([("-return units * 100", DiffLineKind.Removed), ("+return units * pow10(exponent)", DiffLineKind.Added)], edit.Lines.Select(line => (line.Text, line.Kind)));
            });

    [Fact]
    public void AWrittenFileShowsEveryLineAsAdded() =>
        ViewModelScript.Given(EditDiffViewModel.Of("TODO.md", "- [ ] a task\nexport {}"))
            .Then(edit => Assert.Equal([DiffLineKind.Added, DiffLineKind.Added], edit.Lines.Select(line => line.Kind)));

    [Theory]
    [InlineData("src/money.ts", "ts")]
    [InlineData("src\\App.axaml.cs", "cs")]
    [InlineData("deploy/Dockerfile", "Dockerfile")]
    public void AnEditIsColoredInTheLanguageOfItsFile(string path, string language) =>
        ViewModelScript.Given(EditDiffViewModel.Of(path, "- a\n+ b"))
            .Then(edit => Assert.Equal((language, "-a\n+b"), (edit.Language, edit.Diff)));

    [Fact]
    public void ALongEditShowsItsFirstLinesAndSaysHowManyMoreThereAre() =>
        ViewModelScript.Given(EditDiffViewModel.Of("src/money.ts", string.Join('\n', Enumerable.Range(0, EditDiffViewModel.ShownLines + 5).Select(line => $"+ line {line}"))))
            .Then(edit => Assert.Equal((EditDiffViewModel.ShownLines + 1, "… 5 more lines"), (edit.Lines.Count, edit.Lines[^1].Text)));
}
