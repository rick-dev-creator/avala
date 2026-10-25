using Avala.Server.Instances.Domain;

namespace Avala.Server.Instances.Infrastructure.Storage;

internal static class InstanceQueries
{
    public static IQueryable<Instance> Active(this IQueryable<Instance> instances) =>
        instances.Where(instance => instance.RevokedAt == null);
}
