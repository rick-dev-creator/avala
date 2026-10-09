using Avala.Sdk;
using Avala.Testing;
using Avala.Verification.FileSystem;

namespace Avala.Verification.Tests.FileSystem;

public sealed class RepositoryDeclarationsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheDeclarationIsReadFromItsFixedPathInTheWorktreeAndIsAbsentWithoutItAsync()
    {
        using var declared = new TemporaryFolder();
        using var undeclared = new TemporaryFolder();
        const string Declaration = """{ "checks": [] }""";
        Directory.CreateDirectory(Path.Combine(declared.Path, ".avala"));
        await File.WriteAllTextAsync(Path.Combine(declared.Path, ".avala", "checks.json"), Declaration, Cancellation);
        var declarations = new RepositoryDeclarations();

        Assert.Equal(Option<string>.Some(Declaration), await declarations.ReadAsync(declared.Path, Cancellation));
        Assert.Equal(Option<string>.None, await declarations.ReadAsync(undeclared.Path, Cancellation));
    }
}
