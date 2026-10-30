using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace Avala.Components.UI.Theme;

public sealed class Countdown
{
    public static readonly AttachedProperty<bool> RunningProperty =
        AvaloniaProperty.RegisterAttached<Countdown, Control, bool>("Running");

    public static readonly AttachedProperty<TimeSpan> DurationProperty =
        AvaloniaProperty.RegisterAttached<Countdown, Control, TimeSpan>("Duration", TimeSpan.FromSeconds(6));

    public static readonly AttachedProperty<ICommand> CommandProperty =
        AvaloniaProperty.RegisterAttached<Countdown, Control, ICommand>("Command");

    private static readonly AttachedProperty<Run?> RunProperty =
        AvaloniaProperty.RegisterAttached<Countdown, Control, Run?>("Run");

    private static readonly AttachedProperty<Sightline?> SightlineProperty =
        AvaloniaProperty.RegisterAttached<Countdown, Control, Sightline?>("Sightline");

    static Countdown()
    {
        RunningProperty.Changed.AddClassHandler<Control>((control, _) => Watch(control));
        DurationProperty.Changed.AddClassHandler<Control>((control, _) => Restart(control));
    }

    private Countdown()
    {
    }

    public static bool GetRunning(Control control) => control.GetValue(RunningProperty);

    public static void SetRunning(Control control, bool value) => control.SetValue(RunningProperty, value);

    public static TimeSpan GetDuration(Control control) => control.GetValue(DurationProperty);

    public static void SetDuration(Control control, TimeSpan value) => control.SetValue(DurationProperty, value);

    public static ICommand GetCommand(Control control) => control.GetValue(CommandProperty);

    public static void SetCommand(Control control, ICommand value) => control.SetValue(CommandProperty, value);

    public static bool IsCounting(Control control) => control.GetValue(RunProperty) is not null;

    private static void Watch(Control control)
    {
        control.AttachedToVisualTree -= Attached;
        control.DetachedFromVisualTree -= Detached;
        control.AttachedToVisualTree += Attached;
        control.DetachedFromVisualTree += Detached;
        Refresh(control);
    }

    private static void Attached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
        {
            Refresh(control);
        }
    }

    private static void Detached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control control)
        {
            Unsight(control);
            Stop(control);
        }
    }

    private static void Restart(Control control)
    {
        Stop(control);
        Refresh(control);
    }

    private static void Refresh(Control control)
    {
        if (!GetRunning(control) || TopLevel.GetTopLevel(control) is null)
        {
            Unsight(control);
            Stop(control);

            return;
        }

        if (control.GetValue(SightlineProperty) is not { } sightline)
        {
            sightline = new Sightline(control, () => Refresh(control));
            control.SetValue(SightlineProperty, sightline);
        }

        if (!sightline.IsClear)
        {
            Stop(control);
        }
        else if (control.GetValue(RunProperty) is null)
        {
            Start(control);
        }
    }

    private static void Start(Control control)
    {
        var run = new Run();
        control.SetValue(RunProperty, run);
        control.Classes.Add("counting");

        if (control.RenderTransform is not ScaleTransform)
        {
            control.RenderTransform = new ScaleTransform();
            control.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
        }

        _ = CountAsync(control, run);
    }

    private static async Task CountAsync(Control control, Run run)
    {
        var draining = new Animation
        {
            Duration = GetDuration(control),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(ScaleTransform.ScaleXProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(ScaleTransform.ScaleXProperty, 0d) } },
            },
        };

        await draining.RunAsync(control, run.Token);

        if (!run.Token.IsCancellationRequested && ReferenceEquals(control.GetValue(RunProperty), run))
        {
            Stop(control);

            if (GetCommand(control) is { } command && command.CanExecute(null))
            {
                command.Execute(null);
            }
        }
    }

    private static void Stop(Control control)
    {
        if (control.GetValue(RunProperty) is { } run)
        {
            control.ClearValue(RunProperty);
            control.Classes.Remove("counting");
            run.Dispose();
        }
    }

    private static void Unsight(Control control)
    {
        if (control.GetValue(SightlineProperty) is { } sightline)
        {
            control.ClearValue(SightlineProperty);
            sightline.Close();
        }
    }

    private sealed class Run : IDisposable
    {
        private readonly CancellationTokenSource cancellation = new();

        public CancellationToken Token { get; }

        public Run() => Token = cancellation.Token;

        public void Dispose()
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }
}
