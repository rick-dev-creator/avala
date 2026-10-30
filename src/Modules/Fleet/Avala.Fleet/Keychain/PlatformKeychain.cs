using Avala.Fleet.Contracts;
using Avala.Sdk;

namespace Avala.Fleet.Keychain;

internal static class PlatformKeychain
{
    public const string Service = "avala.fleet";

    public static IKeychain Create() =>
        OperatingSystem.IsWindows() ? new WindowsKeychain()
        : OperatingSystem.IsMacOS() ? new MacKeychain()
        : OperatingSystem.IsLinux() ? new LinuxKeychain()
        : new NoKeychain();

    public static Task<T> OffThreadAsync<T>(Func<T> work, CancellationToken cancellationToken) => Task.Run(work, cancellationToken);
}

internal sealed class NoKeychain : IKeychain
{
    public ValueTask<bool> AvailableAsync(CancellationToken cancellationToken) => ValueTask.FromResult(false);

    public ValueTask<Result<string, KeychainFailure>> StoreAsync(string account, string secret, CancellationToken cancellationToken) =>
        ValueTask.FromResult<Result<string, KeychainFailure>>(KeychainFailure.Unavailable);

    public ValueTask<Result<Option<string>, KeychainFailure>> ReadAsync(string account, CancellationToken cancellationToken) =>
        ValueTask.FromResult<Result<Option<string>, KeychainFailure>>(KeychainFailure.Unavailable);

    public ValueTask<Result<string, KeychainFailure>> RemoveAsync(string account, CancellationToken cancellationToken) =>
        ValueTask.FromResult<Result<string, KeychainFailure>>(KeychainFailure.Unavailable);
}
