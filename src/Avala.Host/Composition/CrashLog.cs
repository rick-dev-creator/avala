using Avala.Runtime.Diagnostics;
using Avala.Sdk;
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

    public void Unhandled(UnhandledExceptionEventArgs unhandled)
    {
        var exception = (unhandled.ExceptionObject as Exception).ToOption();

        if (unhandled.IsTerminating)
        {
            log.WriteNow(LogLevel.Critical, Category, "Avala stopped on an unhandled exception", exception);
        }
        else
        {
            log.Write(LogLevel.Error, Category, "An unhandled exception reached the application", exception);
        }
    }

    public void Unobserved(UnobservedTaskExceptionEventArgs unobserved) =>
        log.Write(LogLevel.Error, Category, "A task failed and nothing observed it", unobserved.Exception);

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

    public void Started(string message) => log.Write(LogLevel.Information, Category, message, Option<Exception>.None);

    public void Failed(Exception failure) => log.WriteNow(LogLevel.Critical, Category, "Avala could not start", failure);

    private void OnDomainException(object? sender, UnhandledExceptionEventArgs e) => Unhandled(e);

    private void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs e) => Unobserved(e);

    private void OnDispatcherException(object? sender, DispatcherUnhandledExceptionEventArgs e) =>
        log.WriteNow(LogLevel.Critical, Category, "The interface failed on an unhandled exception", e.Exception);
}
