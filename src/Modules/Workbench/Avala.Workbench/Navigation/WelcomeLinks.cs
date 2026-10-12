using Avala.Jobs.Contracts;
using Avala.Workbench.NewJob;
using Avala.Workbench.Settings;
using Avala.Workbench.Welcome;

namespace Avala.Workbench.Navigation;

internal sealed class SettingsLink(SettingsViewModel settings, JobFocus focus)
{
    public void Open() => focus.Show(settings);

    public Task ConnectAsync()
    {
        Open();

        return settings.Machine.SourceControl.ConnectCommand.ExecuteAsync(null);
    }
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
