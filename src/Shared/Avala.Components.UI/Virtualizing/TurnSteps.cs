using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Avala.Components.UI.Virtualizing;

public sealed class TurnSteps
{
    private const double Settled = 1;

    public static readonly AttachedProperty<IReadOnlyList<int>> StartsProperty =
        AvaloniaProperty.RegisterAttached<TurnSteps, ItemsControl, IReadOnlyList<int>>("Starts", []);

    public static readonly AttachedProperty<ItemsControl> ForProperty =
        AvaloniaProperty.RegisterAttached<TurnSteps, InputElement, ItemsControl>("For");

    public static KeyGesture PreviousGesture { get; } = new(Key.Up, KeyModifiers.Alt);

    public static KeyGesture NextGesture { get; } = new(Key.Down, KeyModifiers.Alt);

    static TurnSteps() => ForProperty.Changed.AddClassHandler<InputElement>((element, change) =>
    {
        element.KeyBindings.RemoveAll(binding => binding.Command is Step);

        if (change.GetNewValue<ItemsControl>() is { } items)
        {
            element.KeyBindings.Add(new KeyBinding { Gesture = PreviousGesture, Command = new Step(() => Previous(items)) });
            element.KeyBindings.Add(new KeyBinding { Gesture = NextGesture, Command = new Step(() => Next(items)) });
        }
    });

    private TurnSteps()
    {
    }

    public static IReadOnlyList<int> GetStarts(ItemsControl items) => items.GetValue(StartsProperty);

    public static void SetStarts(ItemsControl items, IReadOnlyList<int> value) => items.SetValue(StartsProperty, value);

    public static ItemsControl GetFor(InputElement element) => element.GetValue(ForProperty);

    public static void SetFor(InputElement element, ItemsControl value) => element.SetValue(ForProperty, value);

    public static bool Previous(ItemsControl items) => Move(items, (starts, top) => starts.LastOrDefault(start => start < top.Index || (start == top.Index && top.Offset > Settled), -1));

    public static bool Next(ItemsControl items) => Move(items, (starts, top) => starts.FirstOrDefault(start => start > top.Index, -1));

    private static bool Move(ItemsControl items, Func<IReadOnlyList<int>, (int Index, double Offset), int> pick)
    {
        if (items.ItemsPanelRoot is not TimelinePanel panel || panel.Top.Index < 0)
        {
            return false;
        }

        var target = pick(GetStarts(items), panel.Top);

        if (target < 0 || target >= items.ItemCount)
        {
            return false;
        }

        panel.BringToTop(target);

        return true;
    }

    private sealed class Step(Func<bool> move) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _ = move();
    }
}
