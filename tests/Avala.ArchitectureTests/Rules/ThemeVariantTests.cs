using System.Xml.Linq;
using Avala.ArchitectureTests.Solution;
using Avala.ArchitectureTests.Views;

namespace Avala.ArchitectureTests.Rules;

public sealed class ThemeVariantTests
{
    [Fact]
    public async Task EveryThemeResourceHasADarkAndALightValueAsync()
    {
        var files = await XamlFile.ReadAllAsync(SolutionLayout.FilesUnder(SolutionLayout.SourceDirectory, "*.axaml"), TestContext.Current.CancellationToken);
        var themes = files.Where(file => !file.IsView).ToList();

        Assert.Contains(themes, file => file.Path.EndsWith("Tokens.axaml", StringComparison.Ordinal));
        Assert.Empty(themes.SelectMany(file => ThemeVariants.Findings(Path.GetRelativePath(SolutionLayout.Root.FullName, file.Path), file.Document)));
    }

    [Fact]
    public void AVariantMissingAKeyOrAColorOutsideTheDictionariesIsFound()
    {
        var document = XDocument.Parse(
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Dark">
                  <SolidColorBrush x:Key="SurfaceBrush" Color="#101114" />
                  <SolidColorBrush x:Key="TextBrush" Color="#EDEDEF" />
                </ResourceDictionary>
                <ResourceDictionary x:Key="Light">
                  <SolidColorBrush x:Key="SurfaceBrush" Color="#FBFBFC" />
                </ResourceDictionary>
              </ResourceDictionary.ThemeDictionaries>
              <BoxShadows x:Key="ShadowCard">0 8 20 -8 #80000000</BoxShadows>
              <Style Selector="Button#Add" />
            </ResourceDictionary>
            """,
            LoadOptions.SetLineInfo);

        Assert.Equal(
            [
                "Theme.axaml:2 defines TextBrush for Dark only; add its Light value",
                "Theme.axaml:11 holds the color #80000000 outside a theme dictionary; move it into the Dark and Light dictionaries",
            ],
            ThemeVariants.Findings("Theme.axaml", document));
    }

    [Fact]
    public void ADictionaryWithoutTheLightVariantIsFound()
    {
        var document = XDocument.Parse(
            """
            <ResourceDictionary xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ResourceDictionary.ThemeDictionaries>
                <ResourceDictionary x:Key="Dark" />
              </ResourceDictionary.ThemeDictionaries>
            </ResourceDictionary>
            """,
            LoadOptions.SetLineInfo);

        Assert.Equal(["Theme.axaml:2 declares no Light variant; every theme dictionary has both Dark and Light"], ThemeVariants.Findings("Theme.axaml", document));
    }
}
