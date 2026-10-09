using IType = ArchUnitNET.Domain.IType;

namespace Avala.ArchitectureTests.Scopes;

internal static class DependencyTargets
{
    extension(IType type)
    {
        public IEnumerable<IType> NamedTargets =>
            type.Dependencies.Select(dependency => dependency.Target).Where(target => target.Namespace is not null);

        public bool DependsOnCompilerGeneratedTypes =>
            type.Dependencies.Any(dependency => dependency.Target.Namespace is null);
    }
}
