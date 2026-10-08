using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using Avala.Sdk;

namespace Avala.Host.Composition;

internal static class PluginLoader
{
    public static IReadOnlyList<IPlugin> Load(string directory)
    {
        var folders = Directory.Exists(directory)
            ? Directory.EnumerateDirectories(directory).Where(folder => File.Exists(EntryOf(folder))).Order(StringComparer.Ordinal).ToList()
            : [];
        var resolvers = folders.Select(folder => new AssemblyDependencyResolver(EntryOf(folder))).ToList();

        AssemblyLoadContext.Default.Resolving += (context, name) =>
            resolvers.Select(resolver => resolver.ResolveAssemblyToPath(name)).FirstOrDefault(path => path is not null) is { } path
                ? context.LoadFromAssemblyPath(path)
                : null;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (_, name) =>
            resolvers.Select(resolver => resolver.ResolveUnmanagedDllToPath(name)).FirstOrDefault(path => path is not null) is { } path
                ? NativeLibrary.Load(path)
                : IntPtr.Zero;

        return [.. folders.SelectMany(folder => CreatePlugins(AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(Path.GetFileName(folder)))))];
    }

    private static string EntryOf(string folder) => Path.Combine(folder, $"{Path.GetFileName(folder)}.dll");

    private static IEnumerable<IPlugin> CreatePlugins(Assembly assembly) =>
        assembly.GetExportedTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && typeof(IPlugin).IsAssignableFrom(type))
            .Select(type => (IPlugin)Activator.CreateInstance(type)!);
}
