using Avala.Jobs.Contracts;
using Avala.Workbench.NewJob;
using Avala.Workbench.Readying;
using Avala.Workbench.RepositoryRules;
using Avala.Workbench.Settings;
using Avala.Workbench.Welcome;

namespace Avala.Workbench.Navigation;

internal sealed class SettingsLink(SettingsViewModel settings, JobFocus focus)
{
    public void Open() => focus.Show(settings);

    public async Task DeclareChecksAsync(string repository)
    {
        Open();
        await settings.Loading;
        var rules = settings.Repository;
        await rules.OpenCommand.ExecuteAsync(repository);

        if (rules.Files.FirstOrDefault(file => file.Path == RuleFiles.Checks) is { } checks)
        {
            await rules.EditHereCommand.ExecuteAsync(checks);
        }
    }

    public Task ConnectAsync()
    {
        Open();

        return settings.Machine.Accounts.SourceControl.ConnectCommand.ExecuteAsync(null);
    }
}

internal sealed class ReadinessLinks(Func<SettingsLink> settings) : IReadinessLinks
{
    public Task DeclareChecksAsync(string repository) => settings().DeclareChecksAsync(repository);

    public void OpenSettings() => settings().Open();
}

internal sealed class WelcomeLinks(JobFocus focus, SettingsLink settings, NewJobViewModel newJob) : IWelcomeLinks
{
    public void Select(JobId job) => focus.Select(job);

    public Task StartAsync(RepositoryStart start)
    {
        focus.Show(newJob);

        return start switch
        {
            RepositoryStart.OpenFolder => newJob.Repositories.OpenFolderCommand.ExecuteAsync(null),
            RepositoryStart.Clone => newJob.Repositories.CloneCommand.ExecuteAsync(null),
            _ => newJob.Repositories.NewRepositoryCommand.ExecuteAsync(null),
        };
    }

    public Task ConnectAsync() => settings.ConnectAsync();

    public void OpenSettings() => settings.Open();
}
