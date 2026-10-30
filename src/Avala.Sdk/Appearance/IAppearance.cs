using Avala.Sdk.Events;

namespace Avala.Sdk.Appearance;

public enum ThemeChoice
{
    System,
    Light,
    Dark,
    Tide,
    Unit,
    Ash,
    Bloom,
    Signal,
}

public enum MotionChoice
{
    System,
    Reduced,
    Full,
}

public enum ReadingChoice
{
    Comfortable,
    Wide,
}

public enum SendKey
{
    Enter,
    CommandEnter,
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

public sealed record AppearancePreference(ThemeChoice Theme, MotionChoice Motion, ReadingChoice Reading = ReadingChoice.Comfortable)
{
    public static AppearancePreference Default { get; } = new(ThemeChoice.System, MotionChoice.System);

    public SendKey Send { get; init; }

    public bool HidesPersonalDetails { get; init; } = true;
}

public sealed record AppearanceSettings(AppearancePreference Preference, AppearanceFileStatus File, Option<AppearanceError> Error);

public sealed record AppearanceChanged(AppearancePreference Preference) : IIntegrationEvent;

public interface IAppearance
{
    ValueTask<AppearanceSettings> ReadAsync(CancellationToken cancellationToken);

    ValueTask<Result<AppearanceSettings, AppearanceError>> ChangeAsync(AppearancePreference preference, CancellationToken cancellationToken);
}
