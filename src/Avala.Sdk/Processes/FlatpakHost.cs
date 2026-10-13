using System.Collections;
using System.Diagnostics;

namespace Avala.Sdk.Processes;

public static class FlatpakHost
{
    public const string InfoFile = "/.flatpak-info";

    public const string Spawn = "/usr/bin/flatpak-spawn";

    public const string LoginShell = "exec \"$@\"";

    public static bool IsSandboxed { get; } = File.Exists(InfoFile);

    public static ProcessStartInfo Prepared(ProcessStartInfo info) =>
        IsSandboxed ? OnHost(info, Inherited()) : info;

    public static ProcessStartInfo OnHost(ProcessStartInfo info, IReadOnlyDictionary<string, string> inherited)
    {
        var host = new ProcessStartInfo(Spawn)
        {
            RedirectStandardInput = info.RedirectStandardInput,
            RedirectStandardOutput = info.RedirectStandardOutput,
            RedirectStandardError = info.RedirectStandardError,
            StandardOutputEncoding = info.StandardOutputEncoding,
            StandardErrorEncoding = info.StandardErrorEncoding,
            UseShellExecute = false,
            CreateNoWindow = info.CreateNoWindow,
        };

        string[] spawn =
        [
            "--host",
            "--watch-bus",
            .. info.WorkingDirectory is { Length: > 0 } directory ? [$"--directory={directory}"] : Array.Empty<string>(),
            .. info.Environment
                .Where(variable => variable.Value is not null && (!inherited.TryGetValue(variable.Key, out var value) || value != variable.Value))
                .OrderBy(variable => variable.Key, StringComparer.Ordinal)
                .Select(variable => $"--env={variable.Key}={variable.Value}"),
            .. inherited.Keys
                .Where(name => !info.Environment.ContainsKey(name))
                .Order(StringComparer.Ordinal)
                .Select(name => $"--unset-env={name}"),
            "sh", "-lc", LoginShell, "sh",
            info.FileName,
            .. info.ArgumentList,
        ];

        foreach (var argument in spawn)
        {
            host.ArgumentList.Add(argument);
        }

        return host;
    }

    private static Dictionary<string, string> Inherited() =>
        Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Where(variable => variable.Value is string)
            .ToDictionary(variable => (string)variable.Key, variable => (string)variable.Value!, StringComparer.Ordinal);
}
