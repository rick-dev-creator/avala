using System.Reflection;
using System.Runtime.CompilerServices;
using Avala.Server.ArchitectureTests.Layout;

namespace Avala.Server.ArchitectureTests.Rules;

internal static class VisibilityRules
{
    public const string Entry = "ModuleInstaller";

    public static IEnumerable<string> PublicTypesBesidesTheEntry(ServerScope scope) =>
        scope.Assemblies.SelectMany(owner => owner.Assembly.GetExportedTypes()
            .Where(type => owner.Layer != Layer.Module || type.Name != Entry)
            .Select(type => type.FullName ?? type.Name));

    public static IEnumerable<string> InternalsVisibleAgainstTheLayers(ServerScope scope) =>
        scope.Assemblies.SelectMany(owner => owner.Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName.Split(',')[0].Trim())
            .Where(target => !Allows(owner, target))
            .Select(target => $"{owner.Name} -> {target}"));

    private static bool Allows(ModuleAssembly owner, string target) =>
        target == ServerNames.Tests(owner.Module) || target == ServerNames.IntegrationTests(owner.Module) || owner.Layer switch
        {
            Layer.Domain => owner.IsOwn(target, Layer.Application, Layer.Infrastructure),
            Layer.Contracts => owner.IsOwn(target, Layer.Application, Layer.Infrastructure, Layer.EndPoints) || target is ServerNames.Host or ServerNames.HostTests,
            Layer.Application => owner.IsOwn(target, Layer.Module),
            Layer.Infrastructure => owner.IsOwn(target, Layer.Module) || target == ServerNames.Host,
            Layer.EndPoints => owner.IsOwn(target, Layer.Module),
            _ => false,
        };
}
