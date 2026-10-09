using Avala.Sdk.Events;

namespace Avala.Sdk.Appearance;

public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

public enum MotionChoice
{
    System,
    Reduced,
    Full,
}

public enum AppearanceFileStatus
{
    Absent,
    Applied,
    Rejected,
}

public enum AppearanceError
{
    Unreadable,
    TooLarge,
    Invalid,
    Unwritable,
}

public sealed record AppearancePreference(ThemeChoice Theme, MotionChoice Motion)
{
    public static AppearancePreference Default { get; } = new(ThemeChoice.System, MotionChoice.System);
}

public sealed record AppearanceSettings(AppearancePreference Preference, AppearanceFileStatus File, Option<AppearanceError> Error);

public sealed record AppearanceChanged(AppearancePreference Preference) : IIntegrationEvent;

public interface IAppearance
{
    ValueTask<AppearanceSettings> ReadAsync(CancellationToken cancellationToken);

    ValueTask<Result<AppearanceSettings, AppearanceError>> ChangeAsync(AppearancePreference preference, CancellationToken cancellationToken);
}
