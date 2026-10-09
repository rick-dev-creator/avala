using Avala.ArchitectureTests.Solution;

namespace Avala.ArchitectureTests.Views;

internal sealed record Finding(ViewRule Rule, string Module, string Subject, string Message)
{
    public override string ToString() => Message;

    public static Finding OfType(ViewRule rule, Type type, string message) =>
        new(rule, ModuleOf(type), type.Name, $"{type.FullName}: {message}");

    public static Finding OfFile(ViewRule rule, string path, int line, string message) =>
        new(rule, ModuleOf(path), Path.GetFileName(path), $"{Relative(path)}:{line}: {message}");

    public static string ModuleOf(Type type) =>
        SolutionLayout.SourceProjects.FirstOrDefault(project => project.Name == type.Assembly.GetName().Name)?.Module ?? string.Empty;

    public static string ModuleOf(string path) =>
        Path.GetRelativePath(SolutionLayout.SourceDirectory, path).Split(Path.DirectorySeparatorChar) is ["Modules", var module, ..]
            ? module
            : string.Empty;

    private static string Relative(string path) => Path.GetRelativePath(SolutionLayout.Root.FullName, path);
}
