using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avala.Sdk.Events;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Components.Privacy;

[INotifyPropertyChanged]
public sealed partial class PersonalDetailsViewModel(IAppearance appearance, IUiDispatcher ui) : IPersonalDetailsViewModel, IHandle<AppearanceChanged>
{
    public const string Hide = "Hide personal details";

    public const string Reveal = "Show personal details";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Action))]
    public partial bool IsHidden { get; private set; } = true;

    public string Action => IsHidden ? Reveal : Hide;

    public async ValueTask HandleAsync(AppearanceChanged integrationEvent, CancellationToken cancellationToken) =>
        await ui.InvokeAsync(() => IsHidden = integrationEvent.Preference.HidesPersonalDetails, cancellationToken);

    [RelayCommand]
    private async Task ToggleAsync(CancellationToken cancellationToken)
    {
        var current = (await appearance.ReadAsync(cancellationToken)).Preference;
        var changed = await appearance.ChangeAsync(current with { HidesPersonalDetails = !IsHidden }, cancellationToken);

        if (changed.TryGetValue(out var settings, out _))
        {
            IsHidden = settings.Preference.HidesPersonalDetails;
        }
    }
}
