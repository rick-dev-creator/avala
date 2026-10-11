using Avala.Notifications.Delivering;
using Avala.Sdk;

namespace Avala.Notifications.Channels;

internal sealed class EnvironmentSecrets : ISecrets
{
    public Option<string> Of(string variable) => Environment.GetEnvironmentVariable(variable).ToOption();
}
