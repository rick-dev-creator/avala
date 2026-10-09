using System.Windows.Input;

namespace Avala.Components.UI.Markdown;

public sealed class OpensNothing : ICommand
{
    private OpensNothing()
    {
    }

    public event EventHandler? CanExecuteChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    public static OpensNothing Instance { get; } = new();

    public bool CanExecute(object? parameter) => false;

    public void Execute(object? parameter)
    {
    }
}
