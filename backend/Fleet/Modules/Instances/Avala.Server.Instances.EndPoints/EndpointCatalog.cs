using System.Reflection;

namespace Avala.Server.Instances.EndPoints;

internal static class EndpointCatalog
{
    public static Assembly Assembly => typeof(EndpointCatalog).Assembly;
}
