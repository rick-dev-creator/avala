using Microsoft.Extensions.Logging;

namespace Avala.Server.Activity.Infrastructure.Ingestion;

internal static partial class IngestionLog
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "The clock of instance {Instance} is ahead: {Count} events were dated at their reception")]
    public static partial void ClockAhead(ILogger logger, Guid instance, int count);
}
