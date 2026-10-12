using CommunityToolkit.Mvvm.Input;

namespace Avala.Components.Privacy;

public sealed class DesignPersonalDetailsViewModel : IPersonalDetailsViewModel
{
    public bool IsHidden { get; init; } = true;

    public string Action => IsHidden ? PersonalDetailsViewModel.Reveal : PersonalDetailsViewModel.Hide;

    public IAsyncRelayCommand ToggleCommand { get; } = new AsyncRelayCommand(() => Task.CompletedTask);
}
