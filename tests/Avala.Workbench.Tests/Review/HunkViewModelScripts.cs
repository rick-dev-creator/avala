using Avala.Testing;
using Avala.Workbench.Review;
using Avala.Workspaces.Contracts;

namespace Avala.Workbench.Tests.Review;

public sealed class HunkViewModelScripts
{
    [Fact]
    public void AHunkShowsItsHeaderAndMarksEachLine() =>
        ViewModelScript.Given(new HunkViewModel(new DiffHunk(12, 6, 12, 8, "export async function login", [
                new DiffLine(DiffLineKind.Context, "const user = await users.find(email);"),
                new DiffLine(DiffLineKind.Removed, "return ok(user);"),
                new DiffLine(DiffLineKind.Added, "return limited(user);"),
            ])))
            .Then(hunk =>
            {
                Assert.Equal("@@ -12,6 +12,8 @@ export async function login", hunk.Header);
                Assert.Equal([" const user = await users.find(email);", "-return ok(user);", "+return limited(user);"], hunk.Lines);
            });

    [Fact]
    public void AHunkWithoutASectionHasNoTrailingSpace() =>
        ViewModelScript.Given(new HunkViewModel(new DiffHunk(1, 0, 1, 1, string.Empty, [new DiffLine(DiffLineKind.Added, "{}")])))
            .Then(hunk => Assert.Equal("@@ -1,0 +1,1 @@", hunk.Header));
}
