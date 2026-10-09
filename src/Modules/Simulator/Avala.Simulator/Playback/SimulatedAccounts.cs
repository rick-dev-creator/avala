using System.Security.Cryptography;
using System.Text;
using Avala.Agents.Contracts.Connections;
using Avala.Agents.Contracts.Sessions;
using Avala.Sdk;

namespace Avala.Simulator.Playback;

internal static class SimulatedAccounts
{
    public const string ReplaySetting = "replay";

    public static AgentAccount Default { get; } = new("simulated-account", "Simulated account");

    public static AgentAccount For(ConnectionEnvironment connection) =>
        connection.ConfigurationDirectory.Match(
            folder => new AgentAccount($"simulated-login:{folder}", $"Simulated account ({Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))})"),
            () => connection.ApiKey.Match(
                key => new AgentAccount($"simulated-key:{Fingerprint(key.Value)}", "Simulated API key"),
                () => Default));

    public static string Holder(Option<AgentAccount> account) =>
        account.Match(owner => Fingerprint(owner.Id), () => "anonymous");

    public static Option<string> Replay(ConnectionEnvironment connection) =>
        connection.Settings.TryGetValue(ReplaySetting, out var recording) ? recording : Option<string>.None;

    private static string Fingerprint(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
}
