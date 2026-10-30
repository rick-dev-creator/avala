using Avala.Sdk;

namespace Avala.Fleet.Contracts;

public interface IKeychain
{
    ValueTask<bool> AvailableAsync(CancellationToken cancellationToken);

    ValueTask<Result<string, KeychainFailure>> StoreAsync(string account, string secret, CancellationToken cancellationToken);

    ValueTask<Result<Option<string>, KeychainFailure>> ReadAsync(string account, CancellationToken cancellationToken);

    ValueTask<Result<string, KeychainFailure>> RemoveAsync(string account, CancellationToken cancellationToken);
}

public enum KeychainFailure
{
    Unavailable,
    Denied,
    Failed,
}

public sealed record FleetTransport
{
    public const string Handler = "avala.fleet";
}
