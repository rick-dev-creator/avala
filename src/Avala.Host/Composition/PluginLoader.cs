using System.Reflection;
using System.Runtime.Loader;
using Avala.Sdk;

namespace Avala.Host.Composition;

internal static class PluginLoader
{
    public static IEnumerable<IPlugin> Load(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateDirectories(directory).SelectMany(LoadFrom)
            : [];

    private static IEnumerable<IPlugin> LoadFrom(string pluginDirectory)
    {
        var assemblyPath = Path.Combine(pluginDirectory, $"{Path.GetFileName(pluginDirectory)}.dll");

        return File.Exists(assemblyPath)
            ? CreatePlugins(LoadIsolated(assemblyPath))
            : [];
    }

    private static Assembly LoadIsolated(string assemblyPath)
    {
        var context = new AssemblyLoadContext(Path.GetFileNameWithoutExtension(assemblyPath));
        var resolver = new AssemblyDependencyResolver(assemblyPath);

        context.Resolving += (loadContext, name) =>
            resolver.ResolveAssemblyToPath(name) is { } path
                ? loadContext.LoadFromAssemblyPath(path)
                : null;

        return context.LoadFromAssemblyPath(assemblyPath);
    }

    private static IEnumerable<IPlugin> CreatePlugins(Assembly assembly) =>
        assembly.GetExportedTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IPlugin).IsAssignableFrom(type))
            .Select(type => (IPlugin)Activator.CreateInstance(type)!);
}
