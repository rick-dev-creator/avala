using Avala.ArchitectureTests.Source;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Avala.ArchitectureTests.Views;

internal static class SourceViewRules
{
    public const int MaximumCodeBehindLines = 400;
    public const int MaximumViewModelLines = 400;

    public static async Task<IReadOnlyList<Finding>> CodeBehindAsync(ViewScope scope, CancellationToken cancellationToken)
    {
        var files = await SourceFile.ReadAllAsync(scope.FilesUnderSources($"*{ComponentKinds.ViewSuffix}.axaml.cs"), cancellationToken);

        return
        [
            .. files.SelectMany(file => CodeBehindInspector.FindViolations(file.Text)
                .Select(problem => Finding.OfFile(ViewRule.PresentationOnlyCodeBehind, file.Path, 1, problem))),
            .. files.Where(file => LineCount(file.Text) > MaximumCodeBehindLines)
                .Select(file => Finding.OfFile(
                    ViewRule.ComponentSize,
                    file.Path,
                    1,
                    $"{LineCount(file.Text)} lines of code-behind, over {MaximumCodeBehindLines}; move presentation into XAML or split the view")),
        ];
    }

    public static async Task<IReadOnlyList<Finding>> OversizedViewModelsAsync(ViewScope scope, CancellationToken cancellationToken)
    {
        var files = await SourceFile.ReadAllAsync(scope.FilesUnderSources("*ViewModel.cs"), cancellationToken);

        return
        [
            .. files.SelectMany(file => TypeSizeMeter.Measure([file.Text])
                .Where(type => type.Key.EndsWith(ComponentKinds.ViewModelSuffix, StringComparison.Ordinal) && type.Value > MaximumViewModelLines)
                .Select(type => Finding.OfFile(
                    ViewRule.ComponentSize,
                    file.Path,
                    1,
                    $"{type.Key} spans {type.Value} lines, over the {MaximumViewModelLines} a view model may have; split it into child view models"))),
        ];
    }

    public static async Task<IReadOnlyList<Finding>> WithoutScriptsAsync(ViewScope scope, CancellationToken cancellationToken)
    {
        var files = await SourceFile.ReadAllAsync(
            scope.ScriptDirectories.SelectMany(directory => Solution.SolutionLayout.FilesUnder(directory, $"*{ComponentKinds.ScriptsSuffix}.cs")),
            cancellationToken);
        var scripts = files
            .SelectMany(file => CSharpSyntaxTree.ParseText(file.Text, cancellationToken: cancellationToken).GetRoot(cancellationToken)
                .DescendantNodes().OfType<TypeDeclarationSyntax>().Select(type => type.Identifier.Text))
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. scope.Types.Where(type => (type.IsViewModel || type.IsView) && !scripts.Contains($"{type.Name}{ComponentKinds.ScriptsSuffix}"))
                .Select(type => Finding.OfType(
                    ViewRule.ScriptedAcceptanceTests,
                    type,
                    $"has no scripted acceptance tests; add {type.Name}{ComponentKinds.ScriptsSuffix} in a file of the same name, {(type.IsView ? "driving the view headless with ViewScript" : "driving the view model with ViewModelScript")}")),
        ];
    }

    private static int LineCount(string text) => text.Split('\n').Length - (text.EndsWith('\n') ? 1 : 0);
}
