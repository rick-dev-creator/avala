using Avala.Server.Instances.Contracts.Registration;

namespace Avala.Server.Instances.EndPoints.Registration;

internal static class RegisterInstanceMapper
{
    public static RegisterInstance ToCommand(RegisterInstanceRequest request) => new(request.Name);

    public static RegisterInstanceResponse ToResponse(RegisteredInstance instance) =>
        new(instance.Id, instance.Name, instance.RegisteredAt, instance.Key);
}
