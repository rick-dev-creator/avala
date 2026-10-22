using Avala.Sdk;

namespace Avala.Resources.Leasing;

internal interface IPortClaims
{
    ValueTask<IReadOnlySet<int>> ListenedAsync(CancellationToken cancellationToken);

    Option<IDisposable> TryClaim(int first, int last);
}
