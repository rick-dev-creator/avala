using Avala.Forges.Accounts;
using Avala.Forges.Connecting;
using Avala.Forges.Contracts;
using Avala.Forges.Policy;
using Avala.Forges.RuleFiles;
using Avala.Forges.Transport;
using Avala.Sdk;
using Avala.Sdk.Processes;
using Avala.Testing;

namespace Avala.Forges.Tests.Accounts;

public sealed class ForgeConnectionsTests : IAsyncDisposable
{
    private const string Declared = """
        {
          "pollSeconds": 120,
          "forges": [
            { "name": "work", "forge": "github", "credential": { "source": "env", "reference": "WORK_TOKEN" } }
          ]
        }
        """;

    private readonly TemporaryFolder data = new();
    private readonly RecordingBus bus = new();
    private readonly ForgeFile file;
    private readonly ForgeHttp http = new();

    public ForgeConnectionsTests() => file = new ForgeFile(new AvalaPaths(data.Path));

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private string Location => Path.Combine(data.Path, ForgeFileParser.File);

    [Fact]
    public async Task ConnectingAnAccountAddsItBesideTheOthersAndKeepsThePollIntervalAsync()
    {
        await File.WriteAllTextAsync(Location, Declared, Cancellation);
        var connections = Connections();

        var catalog = Outcomes.Succeeds(await connections.ConnectAsync(new ForgeConnectionEdit(new ForgeName("codeberg"), "forgejo") { Url = new Uri("https://codeberg.org"), Source = CredentialSource.Cli, Reference = "codeberg" }, Cancellation));

        var written = Outcomes.Succeeds(ForgeFileParser.Parse(await File.ReadAllTextAsync(Location, Cancellation)));
        Assert.Equal(TimeSpan.FromSeconds(120), written.Poll);
        Assert.Equal(
            [
                new ForgeDeclaration(new ForgeName("work"), "github", Option<Uri>.None, CredentialSource.Environment, "WORK_TOKEN"),
                new ForgeDeclaration(new ForgeName("codeberg"), "forgejo", new Uri("https://codeberg.org"), CredentialSource.Cli, "codeberg"),
            ],
            written.Forges);
        Assert.Equal(["work", "codeberg"], catalog.Connections.Select(connection => connection.Name.Value));
        Assert.Equal(catalog, Assert.IsType<ForgeConnectionsChanged>(Assert.Single(bus.Published)).Catalog);
    }

    [Fact]
    public async Task ConnectingANameAlreadyDeclaredReplacesItWhereItWasAsync()
    {
        await File.WriteAllTextAsync(Location, Declared, Cancellation);
        var connections = Connections();
        _ = Outcomes.Succeeds(await connections.ConnectAsync(new ForgeConnectionEdit(new ForgeName("home"), "github") { Source = CredentialSource.Cli }, Cancellation));

        _ = Outcomes.Succeeds(await connections.ConnectAsync(new ForgeConnectionEdit(new ForgeName("work"), "github") { Source = CredentialSource.Cli }, Cancellation));

        Assert.Equal(
            [("work", CredentialSource.Cli), ("home", CredentialSource.Cli)],
            (await Catalog().CatalogAsync(Cancellation)).Connections.Select(connection => (connection.Name.Value, connection.Source)));
    }

    [Fact]
    public async Task RemovingAConnectionTakesOnlyItOutAndAnUnknownOneIsRefusedAsync()
    {
        await File.WriteAllTextAsync(Location, Declared, Cancellation);
        var connections = Connections();
        _ = Outcomes.Succeeds(await connections.ConnectAsync(new ForgeConnectionEdit(new ForgeName("home"), "github"), Cancellation));

        var removed = Outcomes.Succeeds(await connections.RemoveAsync(new ForgeName("work"), Cancellation));

        Assert.Equal(["home"], removed.Connections.Select(connection => connection.Name.Value));
        Assert.Equal(ForgeError.UnknownConnection, Outcomes.FailsWith(await connections.RemoveAsync(new ForgeName("work"), Cancellation)));
    }

    [Theory]
    [InlineData("not a name", "github", null, "Cli", null, ForgeError.InvalidName)]
    [InlineData("mine", "gitlab", null, "Cli", null, ForgeError.UnknownForge)]
    [InlineData("mine", "forgejo", null, "Cli", null, ForgeError.MissingUrl)]
    [InlineData("mine", "forgejo", "ftp://codeberg.org", "Cli", null, ForgeError.InvalidUrl)]
    [InlineData("mine", "github", null, "Environment", " ", ForgeError.MissingReference)]
    public async Task AnInvalidConnectionLeavesTheFileUntouchedAsync(string name, string forge, string? url, string source, string? reference, ForgeError expected)
    {
        await File.WriteAllTextAsync(Location, Declared, Cancellation);
        var edit = new ForgeConnectionEdit(new ForgeName(name), forge)
        {
            Url = url is null ? Option<Uri>.None : new Uri(url),
            Source = Enum.Parse<CredentialSource>(source),
            Reference = reference.ToOption(),
        };

        Assert.Equal(expected, Outcomes.FailsWith(await Connections().ConnectAsync(edit, Cancellation)));
        Assert.Equal(Declared, await File.ReadAllTextAsync(Location, Cancellation));
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task AFileThatCannotBeWrittenIsUnwritableAndKeepsWhatWasDeclaredAsync()
    {
        await File.WriteAllTextAsync(Location, Declared, Cancellation);
        Directory.CreateDirectory(Location + ".writing");

        Assert.Equal(ForgeError.Unwritable, Outcomes.FailsWith(await Connections().ConnectAsync(new ForgeConnectionEdit(new ForgeName("home"), "github"), Cancellation)));
        Assert.Equal(["work"], (await Catalog().CatalogAsync(Cancellation)).Connections.Select(connection => connection.Name.Value));
    }

    public async ValueTask DisposeAsync()
    {
        await file.DisposeAsync();
        http.Dispose();
        await data.DisposeAsync();
    }

    private ConnectionDesk Connections() => new(file, Kinds, Catalog(), bus);

    private ForgeConnections Catalog() => new(file, Kinds, new ForgeTransports(new Unrun(), http) { Environment = _ => "set" });

    private static IReadOnlyList<IForge> Kinds => [new Kind("github", new Uri("https://api.github.com")), new Kind("forgejo", Option<Uri>.None)];

    private sealed class Kind(string id, Option<Uri> url) : IForge
    {
        public ForgeInfo Info { get; } = new(id, id) { DefaultUrl = url };

        public Option<CliCall> CliFor(ForgeRequest request, ForgeTarget target) => Option<CliCall>.None;

        public ValueTask<Result<Option<PullRequestRef>, ForgeError>> FindAsync(ForgeContext context, string head, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<PullRequestRef, ForgeError>> OpenAsync(ForgeContext context, PullRequestDraft draft, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<PullRequestState, ForgeError>> ReadAsync(ForgeContext context, PullRequestRef pullRequest, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask<Result<CommentRef, ForgeError>> CommentAsync(ForgeContext context, PullRequestRef pullRequest, string body, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Unrun : IProcessRunner
    {
        public ValueTask<Result<ProcessOutcome, ProcessError>> RunAsync(ProcessRequest request, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<ProcessOutcome, ProcessError>.Failure(ProcessError.NotFound));
    }
}
