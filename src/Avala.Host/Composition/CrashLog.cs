using Avala.Runtime.Diagnostics;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace Avala.Host.Composition;

internal sealed class CrashLog(LogFile log)
{
    public const string Category = "Avala";

    public void Watch()
    {
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTask;
        Dispatcher.UIThread.UnhandledException += OnDispatcherException;
    }

    public void Unwatch()
    {
        AppDomain.CurrentDomain.UnhandledException -= OnDomainException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTask;
        Dispatcher.UIThread.UnhandledException -= OnDispatcherException;
    }

    public void OnDomainException(object? sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;

        if (e.IsTerminating)
        {
            log.WriteNow(LogLevel.Critical, Category, "Avala stopped on an unhandled exception", exception);
        }
        else
        {
            log.Write(LogLevel.Error, Category, "An unhandled exception reached the application", exception);
        }
    }

    public void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e) =>
        log.Write(LogLevel.Error, Category, "A task failed and nothing observed it", e.Exception);

    public async Task ObserveAsync(Task startup)
    {
        try
        {
            await startup;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            log.Write(LogLevel.Critical, Category, "Avala's startup failed", failure);
        }
    }

    public void Started(string message) => log.Write(LogLevel.Information, Category, message, null);

    public void Failed(Exception failure) => log.WriteNow(LogLevel.Critical, Category, "Avala could not start", failure);

    private void OnDispatcherException(object? sender, DispatcherUnhandledExceptionEventArgs e) =>
        log.WriteNow(LogLevel.Critical, Category, "The interface failed on an unhandled exception", e.Exception);
}
