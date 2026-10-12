using CommunityToolkit.Mvvm.Input;

namespace Avala.Components.Privacy;

public interface IPersonalDetailsViewModel
{
    bool IsHidden { get; }

    string Action { get; }

    IAsyncRelayCommand ToggleCommand { get; }
}
