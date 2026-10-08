using Stateless.Reflection;
using Stateless.Graph;

namespace Avala.Testing;

public sealed record StateDiagram(string Expected, string Documented)
{
    private const string RefreshVariable = "AVALA_UPDATE_DIAGRAMS";

    public static async Task<StateDiagram> CompareAsync(
        string fileName,
        string title,
        string source,
        StateMachineInfo machine,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(Repository.Root.FullName, "docs", "diagrams", fileName);
        var expected = Render(title, source, machine);

        if (Environment.GetEnvironmentVariable(RefreshVariable) == "1")
        {
            await File.WriteAllTextAsync(path, expected, cancellationToken);
        }

        return new StateDiagram(expected, await File.ReadAllTextAsync(path, cancellationToken));
    }

    private static string Render(string title, string source, StateMachineInfo machine) =>
        $"""
        # {title}

        Generated from `{source}`. Run the tests with `{RefreshVariable}=1` to refresh it.

        ```mermaid
        {MermaidGraph.Format(machine).ReplaceLineEndings("\n").Trim()}
        ```

        """.ReplaceLineEndings("\n");
}
