using Avala.Sdk;
using Avala.Sdk.Appearance;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.Settings;

internal interface IAppearanceViewModel
{
    string File { get; }

    bool FollowsSystem { get; }

    bool IsLight { get; }

    bool IsDark { get; }

    bool MotionFollowsSystem { get; }

    bool IsMotionReduced { get; }

    bool IsMotionFull { get; }

    string Error { get; }

    IAsyncRelayCommand<ThemeChoice> ChooseThemeCommand { get; }

    IAsyncRelayCommand<MotionChoice> ChooseMotionCommand { get; }

    Task LoadAsync(CancellationToken cancellationToken);
}

[INotifyPropertyChanged]
internal sealed partial class AppearanceViewModel(IAppearance appearance) : IAppearanceViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowsSystem), nameof(IsLight), nameof(IsDark), nameof(MotionFollowsSystem), nameof(IsMotionReduced), nameof(IsMotionFull))]
    public partial AppearancePreference Preference { get; private set; } = AppearancePreference.Default;

    [ObservableProperty]
    public partial string File { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Error { get; private set; } = string.Empty;

    public bool FollowsSystem => Preference.Theme == ThemeChoice.System;

    public bool IsLight => Preference.Theme == ThemeChoice.Light;

    public bool IsDark => Preference.Theme == ThemeChoice.Dark;

    public bool MotionFollowsSystem => Preference.Motion == MotionChoice.System;

    public bool IsMotionReduced => Preference.Motion == MotionChoice.Reduced;

    public bool IsMotionFull => Preference.Motion == MotionChoice.Full;

    public async Task LoadAsync(CancellationToken cancellationToken) => Show(await appearance.ReadAsync(cancellationToken));

    [RelayCommand]
    private Task ChooseThemeAsync(ThemeChoice theme, CancellationToken cancellationToken) =>
        ChangeAsync(Preference with { Theme = theme }, cancellationToken);

    [RelayCommand]
    private Task ChooseMotionAsync(MotionChoice motion, CancellationToken cancellationToken) =>
        ChangeAsync(Preference with { Motion = motion }, cancellationToken);

    private async Task ChangeAsync(AppearancePreference preference, CancellationToken cancellationToken)
    {
        var changed = await appearance.ChangeAsync(preference, cancellationToken);
        Error = changed.Match(_ => string.Empty, AppearancePhrases.Error);

        if (changed.TryGetValue(out var settings, out _))
        {
            Show(settings);
        }
    }

    private void Show(AppearanceSettings settings)
    {
        Preference = settings.Preference;
        File = SettingsPhrases.Status(settings.File, settings.Error);
    }
}

internal static class AppearancePhrases
{
    public static string Error(AppearanceError error) => error switch
    {
        AppearanceError.Unwritable => "appearance.json could not be written, so the change was not kept.",
        _ => "The appearance was refused.",
    };
}
