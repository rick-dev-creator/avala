using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Markdig.Renderers;
using Markdig.Syntax;
using MarkView.Avalonia.Extensions;
using MarkView.Avalonia.Rendering;
using MarkView.Avalonia.Rendering.Blocks;

namespace Avala.Components.UI.Markdown;

public sealed class CopyableCode : IMarkViewExtension, IMarkdownObjectRenderer
{
    public static CopyableCode Instance { get; } = new();

    public void Register(AvaloniaRenderer renderer) => renderer.ReplaceOrAdd<CodeBlockRenderer>(this);

    public bool Accept(RendererBase renderer, Type objectType) => typeof(CodeBlock).IsAssignableFrom(objectType);

    public void Write(RendererBase renderer, MarkdownObject objectToRender)
    {
        if (renderer is not AvaloniaRenderer avalonia || objectToRender is not CodeBlock block)
        {
            return;
        }

        var code = string.Join('\n', block.Lines.Lines?.Take(block.Lines.Count).Select(line => line.Slice.ToString()) ?? []);
        var text = new SelectableTextBlock { Text = code, TextWrapping = TextWrapping.Wrap, Classes = { "markdown-code" } };
        var copy = new Button { Content = "Copy", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Classes = { "copy-code" } };
        copy.Command = new CopyCommand(copy, code);
        var frame = new Border { Child = new Panel { Children = { text, copy } }, Classes = { "markdown-code-block" } };

        if (block is FencedCodeBlock { Info: { Length: > 0 } language })
        {
            frame.Classes.Add($"language-{language}");
        }

        avalonia.WriteBlock(frame);
    }

    private sealed class CopyCommand(Button button, string code) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add
            {
            }

            remove
            {
            }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _ = CopyAsync();

        private async Task CopyAsync()
        {
            if (TopLevel.GetTopLevel(button)?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(code);
                button.Content = "Copied";
            }
        }
    }
}
