using Avala.ClaudeCode.Protocol;
using Avala.Testing;

namespace Avala.ClaudeCode.Tests.Conversations;

public sealed class PlacesTests
{
    [Theory]
    [InlineData("plans/greeting.md", true)]
    [InlineData("plans/GREETING.MD", true)]
    [InlineData("plans/../notes.md", false)]
    [InlineData("plans/../plans/greeting.md", false)]
    [InlineData("plans/./greeting.md", false)]
    [InlineData("plans/sub/greeting.md", false)]
    [InlineData("plans/greeting.txt", false)]
    [InlineData("plans/.md", false)]
    [InlineData("plans/a:b.md", false)]
    [InlineData("plansx/greeting.md", false)]
    [InlineData("greeting.md", false)]
    public void OnlyAMarkdownFileDirectlyInThePlansFolderIsThePlan(string relative, bool plan)
    {
        using var login = new TemporaryFolder();
        var places = Of(login);

        Assert.Equal(plan, places.HoldsPlan(Path.Combine(login.Path, relative.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void APathThatIsNotFullyQualifiedOrCarriesANullIsNeverThePlan()
    {
        using var login = new TemporaryFolder();
        var places = Of(login);

        Assert.False(places.HoldsPlan(Path.Combine("plans", "greeting.md")));
        Assert.False(places.HoldsPlan(Path.Combine(login.Path, "plans", "greet\0ing.md")));
        Assert.False(new Places(places.WorkingDirectory, Path.Combine("relative", "plans")).HoldsPlan(Path.Combine(login.Path, "plans", "greeting.md")));
    }

    [Fact]
    public async Task ALinkInThePlansFolderOrAPlansFolderThatIsALinkIsNeverThePlanAsync()
    {
        using var login = new TemporaryFolder();
        using var elsewhere = new TemporaryFolder();
        var places = Of(login);
        var outside = Path.Combine(elsewhere.Path, "target.md");
        await File.WriteAllTextAsync(outside, "outside", TestContext.Current.CancellationToken);
        var linkedLogin = Path.Combine(elsewhere.Path, "linked-login");
        Directory.CreateDirectory(linkedLogin);

        try
        {
            File.CreateSymbolicLink(Path.Combine(login.Path, "plans", "link.md"), outside);
            Directory.CreateSymbolicLink(Path.Combine(linkedLogin, "plans"), Path.Combine(login.Path, "plans"));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Skip($"This host cannot create symbolic links: {exception.Message}");
        }

        Assert.False(places.HoldsPlan(Path.Combine(login.Path, "plans", "link.md")));
        Assert.False(new Places(places.WorkingDirectory, Path.Combine(linkedLogin, "plans")).HoldsPlan(Path.Combine(linkedLogin, "plans", "greeting.md")));
    }

    private static Places Of(TemporaryFolder login)
    {
        Directory.CreateDirectory(Path.Combine(login.Path, "plans"));

        return new Places(HostPaths.Rooted("/work"), Path.Combine(login.Path, "plans"));
    }
}
