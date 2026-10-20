using Avala.Testing;
using Avala.Workbench.Review;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Review;

public sealed class HunkViewModelScripts
{
    [Fact]
    public void AHunkShowsItsHeaderAndMarksEachLineAddedRemovedOrContext() =>
        ViewModelScript.Given(new HunkViewModel(new DiffHunk(12, 6, 12, 8, "export async function login", [
                new DiffLine(DiffLineKind.Context, "const user = await users.find(email);"),
                new DiffLine(DiffLineKind.Removed, "return ok(user);"),
                new DiffLine(DiffLineKind.Added, "return limited(user);"),
            ]), "src/login.ts"))
            .Then(hunk =>
            {
                Assert.Equal("@@ -12,6 +12,8 @@ export async function login", hunk.Header);
                Assert.Equal([" const user = await users.find(email);", "-return ok(user);", "+return limited(user);"], hunk.Lines.Select(line => line.Text));
                Assert.Equal([(false, false), (false, true), (true, false)], hunk.Lines.Select(line => (line.IsAdded, line.IsRemoved)));
            });

    [Fact]
    public void AHunkWithoutASectionHasNoTrailingSpace() =>
        ViewModelScript.Given(new HunkViewModel(new DiffHunk(1, 0, 1, 1, string.Empty, [new DiffLine(DiffLineKind.Added, "{}")]), "data.json"))
            .Then(hunk => Assert.Equal("@@ -1,0 +1,1 @@", hunk.Header));

    [Fact]
    public void AHunkIsColoredInTheLanguageOfItsFile() =>
        ViewModelScript.Given(new HunkViewModel(new DiffHunk(1, 1, 1, 1, string.Empty, [new DiffLine(DiffLineKind.Removed, "a"), new DiffLine(DiffLineKind.Added, "b")]), "internal/http/routes.go"))
            .Then(hunk => Assert.Equal(("go", "-a\n+b"), (hunk.Language, hunk.Diff)));
}
