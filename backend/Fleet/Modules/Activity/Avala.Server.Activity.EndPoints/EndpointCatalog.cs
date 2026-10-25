using System.Reflection;

namespace Avala.Server.Activity.EndPoints;

internal static class EndpointCatalog
{
    public static Assembly Assembly => typeof(EndpointCatalog).Assembly;
}
