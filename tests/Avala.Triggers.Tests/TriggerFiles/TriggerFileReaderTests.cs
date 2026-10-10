using Avala.Sdk;
using Avala.Testing;
using Avala.Triggers.Catalog;
using Avala.Triggers.Contracts;
using Avala.Triggers.TriggerFiles;

namespace Avala.Triggers.Tests.TriggerFiles;

public sealed class TriggerFileReaderTests
{
    private static readonly string Listed = Repositories.Key("/work/listed");

    private static readonly string Unlisted = Repositories.Key("/work/unlisted");

    private const string Sweep = """{ "triggers": [ { "id": "sweep", "instruction": "Sweep", "schedule": { "everyMinutes": 60 } } ] }""";

    [Fact]
    public async Task OnlyTheRepositoriesTheMachineListsHaveTheirTriggersReadFromTheirCurrentCommitAsync()
    {
        await using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(Path.Combine(data.Path, TriggerFileReader.MachineFile), $$"""{ "repositories": ["{{Listed.Replace("\\", "\\\\", StringComparison.Ordinal)}}"] }""", Triggered.Cancellation);
        var files = new CommittedFiles().With(Listed, TriggerFileReader.RepositoryFile, Sweep).With(Unlisted, TriggerFileReader.RepositoryFile, Sweep);

        var snapshot = await new TriggerCatalog(new TriggerFileReader(files, new AvalaPaths(data.Path))).LoadAsync(Triggered.Cancellation);

        Assert.Equal([new TriggerId(Listed, "sweep")], snapshot.Triggers.Select(trigger => trigger.Id));
        Assert.Equal([(Listed, TriggerFileReader.RepositoryFile)], files.Reads);
        Assert.Equal([TriggerFileStatus.Applied, TriggerFileStatus.Applied], snapshot.Files.Select(file => file.Status));
    }

    [Fact]
    public async Task WithoutTheMachineFileNothingIsReadAndItSaysAbsentAsync()
    {
        await using var data = new TemporaryFolder();
        var files = new CommittedFiles();

        var snapshot = await new TriggerCatalog(new TriggerFileReader(files, new AvalaPaths(data.Path))).LoadAsync(Triggered.Cancellation);

        Assert.Empty(snapshot.Triggers);
        Assert.Empty(files.Reads);
        Assert.Equal(TriggerFileStatus.Absent, Assert.Single(snapshot.Files).Status);
    }

    [Fact]
    public async Task ARepositoryThatCannotBeReadIsUnreadableAndARejectedFileLoadsNothingAsync()
    {
        await using var data = new TemporaryFolder();
        await File.WriteAllTextAsync(
            Path.Combine(data.Path, TriggerFileReader.MachineFile),
            $$"""{ "repositories": ["{{Listed.Replace("\\", "\\\\", StringComparison.Ordinal)}}", "{{Unlisted.Replace("\\", "\\\\", StringComparison.Ordinal)}}"] }""",
            Triggered.Cancellation);
        var files = new CommittedFiles().With(Listed, TriggerFileReader.RepositoryFile, """{ "triggers": [ { "id": "sweep", "instruction": "x", "schedule": { "at": "26:00" } } ] }""");

        var snapshot = await new TriggerCatalog(new TriggerFileReader(files, new AvalaPaths(data.Path))).LoadAsync(Triggered.Cancellation);

        Assert.Empty(snapshot.Triggers);
        Assert.Equal(
            [Option<TriggerError>.None, TriggerError.InvalidTime, TriggerError.Unreadable],
            snapshot.Files.Select(file => file.Error));
    }
}
