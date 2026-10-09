using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Avala.ArchitectureTests.Solution;
using Avala.ArchitectureTests.Source;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Avala.ArchitectureTests.Timing;

internal sealed record TimedWait(string File, int Line, string Symbol)
{
    public override string ToString() => $"{File}:{Line}: {Symbol}: {TimedWaitRules.Alternatives}";
}

internal sealed record TimedWaitException(string File, string Symbol);

internal static partial class TimedWaitRules
{
    public const string Alternatives =
        "Waits on time instead of an event. Await the event or signal that states the thing happened: an integration event, a component's refreshed signal, a TaskCompletionSource or a channel. For behavior that is genuinely about time, use a TimeProvider timer (TimeProvider.CreateTimer), testable with FakeTimeProvider.";

    private static readonly FrozenDictionary<string, string> Methods = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Delay"] = "Task",
        ["Sleep"] = "Thread",
        ["SpinWait"] = "Thread",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Types = FrozenSet.Create(StringComparer.Ordinal, "SpinWait", "PeriodicTimer", "Timer");

    private static readonly FrozenSet<string> Namespaces = FrozenSet.Create(
        StringComparer.Ordinal,
        "System.Threading",
        "System.Threading.Tasks",
        "System.Timers",
        "global::System.Threading",
        "global::System.Threading.Tasks",
        "global::System.Timers");

    public static IEnumerable<string> ProductionFiles =>
        new[] { SolutionLayout.SourceDirectory, SolutionLayout.TestsDirectory, Path.Combine(SolutionLayout.Root.FullName, "scripts") }
            .Where(Directory.Exists)
            .SelectMany(FilesIn)
            .Where(path => !path.StartsWith(FixturesDirectory, StringComparison.Ordinal));

    private static string FixturesDirectory =>
        Path.Combine(SolutionLayout.TestsDirectory, "Avala.ArchitectureTests.Fixtures") + Path.DirectorySeparatorChar;

    public static IEnumerable<string> FilesIn(string directory) => SolutionLayout.FilesUnder(directory, "*.cs");

    public static async Task<IReadOnlyList<TimedWait>> WaitOnTimeAsync(IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var files = await SourceFile.ReadAllAsync(paths, cancellationToken);

        return
        [
            .. files
                .SelectMany(file => WaitsIn(file, cancellationToken))
                .Distinct()
                .OrderBy(wait => wait.File, StringComparer.Ordinal)
                .ThenBy(wait => wait.Line),
        ];
    }

    public static async Task<IReadOnlyList<TimedWaitException>> DocumentedExceptionsAsync(CancellationToken cancellationToken)
    {
        var architecture = await File.ReadAllLinesAsync(Path.Combine(SolutionLayout.Root.FullName, "docs", "architecture.md"), cancellationToken);

        return
        [
            .. architecture
                .Select(line => ExceptionRow().Match(line))
                .Where(row => row.Success)
                .Select(row => new TimedWaitException(row.Groups["file"].Value, row.Groups["symbol"].Value)),
        ];
    }

    public static IReadOnlyList<string> Undocumented(IReadOnlyList<TimedWait> waits, IReadOnlyList<TimedWaitException> exceptions) =>
        [.. waits.Where(wait => !exceptions.Contains(new TimedWaitException(wait.File, wait.Symbol))).Select(wait => wait.ToString())];

    public static IReadOnlyList<string> Unused(IReadOnlyList<TimedWait> waits, IReadOnlyList<TimedWaitException> exceptions) =>
        [
            .. exceptions
                .Where(exception => !waits.Any(wait => wait.File == exception.File && wait.Symbol == exception.Symbol))
                .Select(exception => $"{exception.File}: {exception.Symbol} is documented as an exception but no longer waits on time. Remove the exception."),
        ];

    private static IEnumerable<TimedWait> WaitsIn(SourceFile file, CancellationToken cancellationToken)
    {
        var relative = Path.GetRelativePath(SolutionLayout.Root.FullName, file.Path).Replace(Path.DirectorySeparatorChar, '/');

        return CSharpSyntaxTree.ParseText(file.Text, cancellationToken: cancellationToken)
            .GetRoot(cancellationToken)
            .DescendantNodes()
            .OfType<SimpleNameSyntax>()
            .Select(name => (Name: name, Symbol: SymbolOf(name)))
            .Where(found => found.Symbol.Length > 0)
            .Select(found => new TimedWait(relative, found.Name.GetLocation().GetLineSpan().StartLinePosition.Line + 1, found.Symbol));
    }

    private static string SymbolOf(SimpleNameSyntax name)
    {
        var identifier = name.Identifier.Text;

        return name.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Name == name && Methods.TryGetValue(identifier, out var owner) && Names(access.Expression, owner) =>
                $"{owner}.{identifier}",
            _ when Types.Contains(identifier) && NamesAType(name) => identifier,
            _ => string.Empty,
        };
    }

    private static bool Names(ExpressionSyntax expression, string type) =>
        expression.ToString() is var text && (text == type || Namespaces.Any(space => text == $"{space}.{type}"));

    private static bool NamesAType(SimpleNameSyntax name) => name.Parent switch
    {
        MemberAccessExpressionSyntax access when access.Name == name => Namespaces.Contains(access.Expression.ToString()),
        QualifiedNameSyntax qualified when qualified.Right == name => Namespaces.Contains(qualified.Left.ToString()),
        _ => true,
    };

    [GeneratedRegex(@"^\| `RS0030` \| `(?<file>[^`]+)`, `(?<symbol>[^`]+)` \|")]
    private static partial Regex ExceptionRow();
}
