using Avala.Server.Instances.Contracts.Revocation;

namespace Avala.Server.Instances.EndPoints.Revocation;

internal static class RevokeInstanceMapper
{
    public static RevokeInstance ToCommand(RevokeInstanceRequest request) => new(request.Id);
}
