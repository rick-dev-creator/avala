using Avala.Permissions.Contracts;
using Avala.Permissions.PolicyFiles;
using Avala.Testing;

namespace Avala.Permissions.Tests.PolicyFiles;

public sealed class PolicyFileReaderTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AWorkingDirectoryWithoutAPolicyFileHasNoRepositoryRulesAsync()
    {
        using var folder = new TemporaryFolder();

        Assert.True(Outcomes.Succeeds(await new PolicyFileReader().ReadAsync(folder.Path, Cancellation)).IsNone);
    }

    [Fact]
    public async Task ThePolicyFileOfTheWorkingDirectoryIsReadAndParsedAsync()
    {
        using var folder = new TemporaryFolder();
        await WriteAsync(folder, """{ "rules": [ { "name": "tests", "kind": "command", "target": "dotnet test", "answer": "allow" } ] }""");

        var rules = Outcomes.Present(Outcomes.Succeeds(await new PolicyFileReader().ReadAsync(folder.Path, Cancellation)));

        Assert.Equal("tests", Assert.Single(rules).Name);
    }

    [Fact]
    public async Task APolicyFileOverTheSizeLimitIsRejectedUnparsedAsync()
    {
        using var folder = new TemporaryFolder();
        await WriteAsync(folder, $$"""{ "rules": [], "padding": "{{new string(' ', PolicyFileReader.MaximumBytes)}}" }""");

        Assert.Equal(PolicyError.TooLarge, Outcomes.FailsWith(await new PolicyFileReader().ReadAsync(folder.Path, Cancellation)));
    }

    private static async Task WriteAsync(TemporaryFolder folder, string content)
    {
        Directory.CreateDirectory(Path.Combine(folder.Path, ".avala"));
        await File.WriteAllTextAsync(Path.Combine(folder.Path, ".avala", "permissions.json"), content, Cancellation);
    }
}
