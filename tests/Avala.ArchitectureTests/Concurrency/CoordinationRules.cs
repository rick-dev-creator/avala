using System.Collections.Frozen;
using Avala.ArchitectureTests.Solution;
using Avala.ArchitectureTests.Source;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Avala.ArchitectureTests.Concurrency;

internal static class CoordinationRules
{
    private static readonly FrozenSet<string> Primitives = FrozenSet.Create(
        StringComparer.Ordinal,
        "Lock",
        "Monitor",
        "SemaphoreSlim",
        "Semaphore",
        "Mutex",
        "ReaderWriterLock",
        "ReaderWriterLockSlim",
        "SpinLock",
        "SpinWait",
        "Barrier",
        "CountdownEvent",
        "ManualResetEvent",
        "ManualResetEventSlim",
        "AutoResetEvent",
        "EventWaitHandle",
        "WaitHandle",
        "ConcurrentDictionary",
        "ConcurrentQueue",
        "ConcurrentStack",
        "ConcurrentBag",
        "BlockingCollection");

    private static readonly FrozenSet<string> Namespaces = FrozenSet.Create(
        StringComparer.Ordinal,
        "System.Threading",
        "System.Collections.Concurrent");

    public static async Task<IReadOnlyList<string>> CoordinateThreadsAsync(string directory, CancellationToken cancellationToken)
    {
        var files = await SourceFile.ReadAllAsync(SolutionLayout.FilesUnder(directory, "*.cs"), cancellationToken);

        return
        [
            .. files
                .SelectMany(file => PrimitivesIn(file, cancellationToken)
                    .Select(primitive => $"{Path.GetFileNameWithoutExtension(file.Path)}: {primitive}"))
                .Distinct()
                .Order(StringComparer.Ordinal),
        ];
    }

    private static IEnumerable<string> PrimitivesIn(SourceFile file, CancellationToken cancellationToken) =>
        CSharpSyntaxTree.ParseText(file.Text, cancellationToken: cancellationToken)
            .GetRoot(cancellationToken)
            .DescendantNodes()
            .Select(node => node switch
            {
                LockStatementSyntax => "lock",
                SimpleNameSyntax name when Primitives.Contains(name.Identifier.Text) && NamesAType(name) => name.Identifier.Text,
                _ => string.Empty,
            })
            .Where(primitive => primitive.Length > 0);

    private static bool NamesAType(SimpleNameSyntax name) => name.Parent switch
    {
        MemberAccessExpressionSyntax access when access.Name == name => Namespaces.Contains(access.Expression.ToString()),
        QualifiedNameSyntax qualified when qualified.Right == name => Namespaces.Contains(qualified.Left.ToString()),
        _ => true,
    };
}
