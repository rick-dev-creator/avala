using Avala.Sdk;

namespace Avala.Permissions.Answering;

internal interface IRealPaths
{
    Option<string> Resolve(string path);

    Option<IReadOnlyList<string>> Expand(string pattern);

    Option<string> Repository(string path);
}
