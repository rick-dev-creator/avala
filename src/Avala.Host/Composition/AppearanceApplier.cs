using Avala.Components.UI.Theme;
using Avala.Sdk;
using Avala.Sdk.Appearance;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Avala.Host.Composition;

internal sealed class AppearanceApplier(Application application)
{
    private AppearancePreference applied = AppearancePreference.Default;
    private bool systemReduced;
    private TopLevel? window;

    public AppearancePreference Applied => applied;

    public bool IsReduced => applied.Motion switch
    {
        MotionChoice.Reduced => true,
        MotionChoice.Full => false,
        _ => systemReduced,
    };

    public static ThemeVariant Variant(ThemeChoice theme) => theme switch
    {
        ThemeChoice.Light => ThemeVariant.Light,
        ThemeChoice.Dark => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    public void FollowSystem(bool prefersReducedMotion)
    {
        systemReduced = prefersReducedMotion;
        Apply(applied);
    }

    public void Attach(TopLevel shown)
    {
        window = shown;
        Apply(applied);
    }

    public void Apply(AppearancePreference preference)
    {
        applied = preference;
        application.RequestedThemeVariant = Variant(preference.Theme);

        if (window is not null)
        {
            Motion.SetIsReduced(window, IsReduced);
        }
    }

    public async Task FollowAsync(IAsyncEnumerable<AppearanceChanged> changes, IUiDispatcher ui, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var changed in changes.WithCancellation(cancellationToken))
            {
                await ui.InvokeAsync(() => Apply(changed.Preference), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}

internal sealed class AppearanceStartup(IAppearance appearance, ISystemMotion system, AppearanceApplier applier)
{
    public async Task<Window> ShowAsync(Func<Window> create, CancellationToken cancellationToken)
    {
        var settings = await appearance.ReadAsync(cancellationToken);
        var prefersReduced = await system.PrefersReducedAsync(cancellationToken);
        applier.FollowSystem(prefersReduced);
        applier.Apply(settings.Preference);
        var window = create();
        applier.Attach(window);
        window.Show();

        return window;
    }
}
