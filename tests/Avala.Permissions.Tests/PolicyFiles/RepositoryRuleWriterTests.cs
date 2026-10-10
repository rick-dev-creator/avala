using Avala.Agents.Contracts.Events;
using Avala.Jobs.Contracts;
using Avala.Permissions.Contracts;
using Avala.Permissions.PolicyFiles;
using Avala.Sdk;
using Avala.Testing;
using Avala.Workspaces.Contracts;

namespace Avala.Permissions.Tests.PolicyFiles;

public sealed class RepositoryRuleWriterTests
{
    private const string Repository = "/repositories/shop";

    private static readonly PolicyRule Always = new(RuleOrigin.Repository, "always in this repository", ItemKind.Command, "dotnet ef database update", RuleScope.Anywhere, PolicyAnswer.Allow);

    private readonly WorkingFiles files = new();
    private readonly JobId job = JobId.New();
    private readonly RepositoryRuleWriter writer;

    public RepositoryRuleWriterTests() => writer = new RepositoryRuleWriter(new OneJobCatalog(job, Repository), files);

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheRuleIsAppendedToTheRepositorysWorkingFileWhichKeepsWhatItDeclaredAndStaysValidAsync()
    {
        files.Content = """{ "autonomy": "supervised", "rules": [ { "name": "no pushes", "kind": "command", "target": "git push*", "answer": "deny" } ] }""";

        Assert.Equal(Always, Outcomes.Succeeds(await writer.AddAsync(job, Always, Cancellation)));

        var written = Outcomes.Succeeds(PolicyFileParser.Parse(Outcomes.Present(files.Content)));
        Assert.Equal(["no pushes", "always in this repository"], written.Repository.Select(rule => rule.Name));
        Assert.Equal(Always, written.Repository[^1]);
        Assert.Equal(Repository, files.WrittenIn);
    }

    [Fact]
    public async Task ARepositoryWithoutAPolicyFileGetsOneHoldingTheRuleAsync()
    {
        Outcomes.Succeeds(await writer.AddAsync(job, Always with { Answer = PolicyAnswer.Deny }, Cancellation));

        Assert.Equal([Always with { Answer = PolicyAnswer.Deny }], Outcomes.Succeeds(PolicyFileParser.Parse(Outcomes.Present(files.Content))).Repository);
    }

    [Fact]
    public async Task AnInvalidPolicyFileIsLeftUntouchedAndItsErrorReportedAsync()
    {
        files.Content = """{ "rules": [ { "kind": "command", "target": "ls*" } ] }""";

        Assert.Equal(PolicyError.MissingAnswer, Outcomes.FailsWith(await writer.AddAsync(job, Always, Cancellation)));
        Assert.Equal(string.Empty, files.WrittenIn);
    }

    [Fact]
    public async Task AJobTheCatalogDoesNotKnowHasNoRepositoryToWriteToAsync()
    {
        Assert.Equal(PolicyError.RepositoryUnwritable, Outcomes.FailsWith(await writer.AddAsync(JobId.New(), Always, Cancellation)));
        Assert.Equal(string.Empty, files.WrittenIn);
    }
}
