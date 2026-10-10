using Avala.Agents.Contracts.Capabilities;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;

namespace Avala.ClaudeCode.Protocol;

internal static class ClaudeModels
{
    public static OffersModels Offered { get; } = new(
        ["best", "fable", "opus", "sonnet", "haiku", "opus[1m]", "sonnet[1m]", "opusplan"],
        ["low", "medium", "high", "xhigh", "max"]);

    public static OffersModels On(ConnectionEnvironment connection) => Offered.WithDefaults(OffersModels.SettingsOf(connection.Settings));

    public static ModelChoice Chosen(SessionOptions options) => options.Model.Or(OffersModels.SettingsOf(options.Connection.Settings));
}
