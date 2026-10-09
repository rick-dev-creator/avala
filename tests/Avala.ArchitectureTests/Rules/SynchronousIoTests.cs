using Avala.ArchitectureTests.Solution;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Avala.ArchitectureTests.Rules;

public sealed class SynchronousIoTests
{
    private const string LastWords = "src/Avala.Runtime/Diagnostics/LastWords.cs";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheBannedApisRejectSynchronousStreamWritesReadsAndFlushesAsync()
    {
        var banned = await File.ReadAllLinesAsync(Path.Combine(SolutionLayout.Root.FullName, "BannedSymbols.txt"), Cancellation);

        Assert.Superset(
            new HashSet<string>(
            [
                "M:System.IO.Stream.Write(System.ReadOnlySpan{System.Byte})",
                "M:System.IO.FileStream.Write(System.ReadOnlySpan{System.Byte})",
                "M:System.IO.Stream.Write(System.Byte[],System.Int32,System.Int32)",
                "M:System.IO.FileStream.Write(System.Byte[],System.Int32,System.Int32)",
                "M:System.IO.Stream.Read(System.Span{System.Byte})",
                "M:System.IO.FileStream.Read(System.Span{System.Byte})",
                "M:System.IO.Stream.Flush",
                "M:System.IO.FileStream.Flush",
            ]),
            banned.Select(line => line.Split(';')[0]).ToHashSet());
    }

    [Fact]
    public async Task OnlyTheDocumentedSourceFilesAreExemptFromTheBannedApisAsync()
    {
        var editorConfig = await File.ReadAllLinesAsync(Path.Combine(SolutionLayout.Root.FullName, ".editorconfig"), Cancellation);
        var section = string.Empty;
        var exempt = new List<string>();

        foreach (var line in editorConfig.Select(line => line.Trim()))
        {
            section = line.StartsWith('[') ? line.Trim('[', ']') : section;

            if (line.Replace(" ", string.Empty, StringComparison.Ordinal) == "dotnet_diagnostic.RS0030.severity=none" && section.StartsWith("src/", StringComparison.Ordinal))
            {
                exempt.Add(section);
            }
        }

        Assert.Equal([LastWords, "src/Avala.Sdk/Domain/GuardedTransitions.cs"], exempt.Order(StringComparer.Ordinal));
        Assert.Contains($"| `RS0030` | `{LastWords}` |", await File.ReadAllTextAsync(Path.Combine(SolutionLayout.Root.FullName, "docs", "architecture.md"), Cancellation), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLastWordsOfADyingProcessAreItsOnlySynchronousWriteAsync()
    {
        var text = await File.ReadAllTextAsync(Path.Combine(SolutionLayout.Root.FullName, LastWords), Cancellation);
        var root = await CSharpSyntaxTree.ParseText(text, cancellationToken: Cancellation).GetRootAsync(Cancellation);
        var calls = root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Select(call => call.Expression)
            .OfType<MemberAccessExpressionSyntax>()
            .Select(access => access.Name.Identifier.Text)
            .Where(name => name is "Write" or "Read" or "Flush" or "WriteByte" or "ReadByte" or "CopyTo")
            .ToList();

        Assert.Equal(["Write"], calls);
    }
}
