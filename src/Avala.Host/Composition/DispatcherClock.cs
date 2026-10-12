using System.Reflection;
using Avalonia.Threading;

namespace Avala.Host.Composition;

internal static class DispatcherClock
{
    public static bool Align()
    {
        var dispatcher = Dispatcher.UIThread;
        var fields = typeof(Dispatcher).GetRuntimeFields().Where(field => !field.IsStatic).ToList();
        var provider = fields.FirstOrDefault(field => field.Name == "_timeProvider");
        if (provider is null || provider.FieldType != typeof(Func<long>)
            || fields.FirstOrDefault(field => field.Name == "_impl")?.GetValue(dispatcher) is not { } platform
            || platform.GetType().GetInterfaces().Select(contract => contract.GetProperty("Now")).FirstOrDefault(now => now?.PropertyType == typeof(long)) is not { } now)
        {
            return false;
        }

        provider.SetValue(dispatcher, new Func<long>(() => (long)now.GetValue(platform)!));
        return true;
    }
}
