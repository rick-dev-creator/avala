namespace Avala.ArchitectureTests.Solution;

internal sealed record SourceProject(string Name, string Path, string? Module)
{
    public ProjectKind Kind =>
        Name.EndsWith(".Contracts", StringComparison.Ordinal) ? ProjectKind.Contracts
        : Name.EndsWith(".UI", StringComparison.Ordinal) ? ProjectKind.UI
        : ProjectKind.Core;

    public static SourceProject From(string sourceDirectory, string projectPath)
    {
        var segments = System.IO.Path.GetRelativePath(sourceDirectory, projectPath)
            .Split(System.IO.Path.DirectorySeparatorChar);
        var module = segments is ["Modules", var name, ..] ? name : null;

        return new SourceProject(System.IO.Path.GetFileNameWithoutExtension(projectPath), projectPath, module);
    }
}
