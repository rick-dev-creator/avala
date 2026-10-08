using Avala.ArchitectureTests.Solution;
using Avala.ArchitectureTests.Source;
using Avalonia.Controls;

namespace Avala.ArchitectureTests.Rules;

public sealed class MvvmTests
{
    private const string ViewModelSuffix = "ViewModel";
    private const string ViewSuffix = "View";

    private static IEnumerable<Type> ViewModels =>
        AvalaAssemblies.AllTypes.Where(type => type.Name.EndsWith(ViewModelSuffix, StringComparison.Ordinal));

    private static IEnumerable<Type> Views =>
        AvalaAssemblies.AllTypes.Where(type =>
            typeof(Control).IsAssignableFrom(type) && type.Name.EndsWith(ViewSuffix, StringComparison.Ordinal));

    [Fact]
    public void ViewModelsLiveInAssembliesThatDoNotKnowAvalonia()
    {
        var violations = ViewModels.Where(type => type.Assembly.ReferencesAvalonia).Select(type => type.FullName);

        Assert.Empty(violations);
    }

    [Fact]
    public void EveryViewModelHasAView()
    {
        var views = Views.Select(type => Stem(type.Name, ViewSuffix));

        Assert.Empty(ViewModels.Select(type => Stem(type.Name, ViewModelSuffix)).Except(views));
    }

    [Fact]
    public void EveryViewHasAViewModel()
    {
        var viewModels = ViewModels.Select(type => Stem(type.Name, ViewModelSuffix));

        Assert.Empty(Views.Select(type => Stem(type.Name, ViewSuffix)).Except(viewModels));
    }

    [Fact]
    public async Task ViewsHaveNoCodeBehindLogicAsync()
    {
        var codeBehind = await SourceFile.ReadAllAsync(
            SolutionLayout.FilesUnder(SolutionLayout.SourceDirectory, $"*{ViewSuffix}.axaml.cs"),
            TestContext.Current.CancellationToken);

        var violations = codeBehind.SelectMany(file =>
            CodeBehindInspector.FindLogic(file.Text).Select(problem => $"{file.Path}: {problem}"));

        Assert.Empty(violations);
    }

    private static string Stem(string name, string suffix) => name[..^suffix.Length];
}
