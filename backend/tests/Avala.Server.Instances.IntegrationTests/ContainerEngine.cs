using DotNet.Testcontainers.Configurations;

namespace Avala.Server.Instances.IntegrationTests;

internal static class ContainerEngine
{
    private const string Linux = "linux";

    public static async Task<(bool Available, string Reason)> ProbeAsync()
    {
        try
        {
            var endpoint = TestcontainersSettings.OS?.DockerEndpointAuthConfig;

            if (endpoint is null)
            {
                return (false, "These tests need a Linux container engine, and Testcontainers found none.");
            }

            using var client = endpoint.GetDockerClientBuilder(Guid.NewGuid()).Build();
            var info = await client.System.GetSystemInfoAsync();

            return info.OSType == Linux
                ? (true, string.Empty)
                : (false, $"These tests need a Linux container engine, and the one at {endpoint.Endpoint} runs {info.OSType} containers.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return (false, $"These tests need a Linux container engine, and none answered: {exception.Message}");
        }
    }
}
