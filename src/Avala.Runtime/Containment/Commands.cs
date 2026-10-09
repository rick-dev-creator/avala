using System.ComponentModel;
using System.Diagnostics;
using Avala.Sdk;

namespace Avala.Runtime.Containment;

internal static class Commands
{
    public static async Task<Option<string>> OutputAsync(string program, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(info);

            if (process is null)
            {
                return Option<string>.None;
            }

            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            _ = await error;

            return process.ExitCode == 0 ? await output : Option<string>.None;
        }
        catch (Win32Exception)
        {
            return Option<string>.None;
        }
    }
}
