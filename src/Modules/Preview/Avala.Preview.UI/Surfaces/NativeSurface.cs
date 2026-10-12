using System.Collections.Frozen;
using Avala.Preview.Contracts;
using Avala.Sdk;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Avala.Preview.UI.Surfaces;

internal sealed class NativeSurface(SurfaceRules rules, SurfaceEmbedding embedding) : IWebSurface, IDisposable
{
    private static readonly FrozenSet<string> InlineSchemes = new[] { "about", "data", "blob" }.ToFrozenSet(StringComparer.Ordinal);

    private NativeWebView? view;
    private NativeWebDialog? dialog;
    private bool closed;

    public event EventHandler<Uri>? Refused;

    public event EventHandler? Changed;

    public SurfaceEmbedding Embedding { get; } = embedding;

    public Option<Uri> Address { get; private set; }

    public bool CanGoBack => view?.CanGoBack ?? dialog?.CanGoBack ?? false;

    private bool OpensInWindow => Embedding == SurfaceEmbedding.Window && !closed;

    public void Navigate(Uri address)
    {
        Address = address;
        Show(address);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reload()
    {
        _ = view?.Refresh();
        _ = dialog?.Refresh();
    }

    public void GoBack()
    {
        _ = view?.GoBack();
        _ = dialog?.GoBack();
    }

    public void Dispose() => Close();

    public void Close()
    {
        closed = true;
        ReleaseView();
        ReleaseDialog();
    }

    public void Attach(NativeWebView host)
    {
        if (closed)
        {
            return;
        }

        Watch(host);
        _ = Address.Map(address => host.Source = address);
    }

    private static bool IsInline(Uri address) => address.IsAbsoluteUri && InlineSchemes.Contains(address.Scheme);

    private void Show(Uri address)
    {
        if (view is not null)
        {
            view.Navigate(address);
        }
        else if (OpensInWindow)
        {
            Dialog().Navigate(address);
        }
    }

    private void Watch(NativeWebView host)
    {
        ReleaseView();
        view = host;
        host.EnvironmentRequested += OnEnvironmentRequested;
        host.NavigationStarted += OnNavigationStarted;
        host.NewWindowRequested += OnNewWindowRequested;
        host.NavigationCompleted += OnNavigationCompleted;
    }

    private void ReleaseView()
    {
        if (view is not null)
        {
            Unwatch(view);
            view = null;
        }
    }

    private void ReleaseDialog()
    {
        if (dialog is not null)
        {
            dialog.NavigationStarted -= OnNavigationStarted;
            dialog.NewWindowRequested -= OnNewWindowRequested;
            dialog.NavigationCompleted -= OnNavigationCompleted;
            dialog.EnvironmentRequested -= OnEnvironmentRequested;
            dialog.Close();
            dialog.Dispose();
            dialog = null;
        }
    }

    private NativeWebDialog Dialog()
    {
        if (dialog is not null)
        {
            return dialog;
        }

        dialog = new NativeWebDialog { Title = "Avala preview", CanUserResize = true };
        dialog.EnvironmentRequested += OnEnvironmentRequested;
        dialog.NavigationStarted += OnNavigationStarted;
        dialog.NewWindowRequested += OnNewWindowRequested;
        dialog.NavigationCompleted += OnNavigationCompleted;
        dialog.Show();

        return dialog;
    }

    private void Unwatch(NativeWebView host)
    {
        host.EnvironmentRequested -= OnEnvironmentRequested;
        host.NavigationStarted -= OnNavigationStarted;
        host.NewWindowRequested -= OnNewWindowRequested;
        host.NavigationCompleted -= OnNavigationCompleted;
    }

    private static void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        e.EnableDevTools = false;

        switch (e)
        {
            case GtkWebViewEnvironmentRequestedEventArgs gtk:
                gtk.EphemeralDataManager = true;
                break;
            case WindowsWebView2EnvironmentRequestedEventArgs edge:
                edge.IsInPrivateModeEnabled = true;
                break;
            case AppleWKWebViewEnvironmentRequestedEventArgs apple:
                apple.NonPersistentDataStore = true;
                break;
        }
    }

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (e.Request is { } address && Refuses(address))
        {
            e.Cancel = true;
            Refuse(address);
        }
    }

    private bool Refuses(Uri address) => !IsInline(address) && !rules.Admits(address);

    private void Refuse(Uri address) => Refused?.Invoke(this, address);

    private void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (e.Request is { } address)
        {
            Follow(address);
        }
    }

    private void Follow(Uri address)
    {
        if (rules.Admits(address))
        {
            Navigate(address);
        }
        else
        {
            Refuse(address);
        }
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        if (e.Request is { } address)
        {
            Address = address;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
