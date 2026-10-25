namespace Avala.Server.Activity.EndPoints.Usage;

internal sealed class ListUsageRequest
{
    public string? From { get; init; }

    public string? To { get; init; }

    public string? Zone { get; init; }

    public string? Step { get; init; }

    public string? Repository { get; init; }
}
