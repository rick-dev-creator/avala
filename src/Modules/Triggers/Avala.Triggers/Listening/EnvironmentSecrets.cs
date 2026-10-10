using Avala.Sdk;
using Avala.Triggers.Receiving;

namespace Avala.Triggers.Listening;

internal sealed class EnvironmentSecrets : ISecrets
{
    public Option<string> Of(string variable) => Environment.GetEnvironmentVariable(variable).ToOption();
}
