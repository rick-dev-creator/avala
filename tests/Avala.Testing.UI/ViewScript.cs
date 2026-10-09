using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Avala.Testing.UI;

public sealed class ViewScript
{
    private static readonly List<Window> Open = [];

    private ViewScript(Window window)
    {
        Window = window;
        Open.Add(window);
        window.Show();
        Settle();
    }

    public Window Window { get; }

    public static ViewScript Show(object viewModel) =>
        new(new Window { Width = 640, Height = 480, Content = viewModel });

    public static ViewScript Show(Control view, object viewModel)
    {
        view.DataContext = viewModel;

        return new ViewScript(new Window { Width = 640, Height = 480, Content = view });
    }

    public static ViewScript Present(Window window, object viewModel)
    {
        window.DataContext = viewModel;

        return new ViewScript(window);
    }

    public T Find<T>(string name)
        where T : Control =>
        Named(name) as T ?? throw new InvalidOperationException($"No {typeof(T).Name} named {name} is on screen.");

    public Control Find(string name) => Find<Control>(name);

    public string TextOf(string name) => Find<TextBlock>(name).Text ?? string.Empty;

    public bool Shows(string name) => Named(name) is { IsEffectivelyVisible: true } control && control.Bounds.Width > 0;

    public bool HasClass(string name, string className) => Find(name).Classes.Contains(className);

    public IReadOnlyList<string> VisibleTexts =>
        [.. Window.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrEmpty(text.Text))
            .Select(text => text.Text!)];

    public IReadOnlyList<T> All<T>()
        where T : Visual =>
        [.. Window.GetVisualDescendants().OfType<T>().Where(visual => visual.IsEffectivelyVisible)];

    public ViewScript Click(string name) => Click(Find(name));

    public ViewScript Click(Control target)
    {
        var center = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException($"{target.Name ?? target.GetType().Name} is not laid out.");
        Window.MouseDown(center, MouseButton.Left);
        Window.MouseUp(center, MouseButton.Left);

        return Settle();
    }

    public ViewScript Type(string name, string text)
    {
        Find(name).Focus();
        Window.KeyTextInput(text);

        return Settle();
    }

    public ViewScript Press(Key key) => Press(key, RawInputModifiers.None);

    public ViewScript Press(Key key, RawInputModifiers modifiers)
    {
        Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Window.KeyRelease(key, modifiers, PhysicalKey.None, null);

        return Settle();
    }

    public ViewScript Settle()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        return this;
    }

    internal static void CloseAll()
    {
        foreach (var window in Open)
        {
            window.Close();
        }

        Open.Clear();
        Dispatcher.UIThread.RunJobs();
    }

    private Control? Named(string name) =>
        Window.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == name)
        ?? Window.GetLogicalDescendants().OfType<Control>().FirstOrDefault(control => control.Name == name);
}
