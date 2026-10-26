namespace Avala.Testing;

public sealed class GitKeepsLineEndings
{
    public GitKeepsLineEndings()
    {
        Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", "1");
        Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", "core.autocrlf");
        Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", "false");
    }
}
