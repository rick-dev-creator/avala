using Avala.Server.Instances.Contracts.Lookup;

namespace Avala.Server.Instances.EndPoints.Lookup;

internal static class FindInstanceMapper
{
    public static FindInstance ToQuery(FindInstanceRequest request) => new(request.Id);

    public static FindInstanceResponse ToResponse(InstanceDetails instance) =>
        new(instance.Id, instance.Name, instance.RegisteredAt);
}
