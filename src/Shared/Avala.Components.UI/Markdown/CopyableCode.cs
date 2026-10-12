using System.Windows.Input;
using Avala.Components.UI.Copying;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
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

    public static InlineCollection Colored(string code, string language)
    {
        var inlines = new InlineCollection();
        var lines = CodeColors.Lines(code, language);

        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0)
            {
                inlines.Add(new LineBreak());
            }

            foreach (var span in lines[index])
            {
                var run = new Run(span.Text);

                if (span.Role.Length > 0)
                {
                    run.Classes.Add($"syntax-{span.Role}");
                    run.Bind(TextElement.ForegroundProperty, run.GetResourceObservable(Brush(span.Role)));
                    run.FontStyle = span.Role == "comment" ? FontStyle.Italic : FontStyle.Normal;
                }

                inlines.Add(run);
            }
        }

        return inlines;
    }

    public void Register(AvaloniaRenderer renderer) => renderer.ReplaceOrAdd<CodeBlockRenderer>(this);

    public bool Accept(RendererBase renderer, Type objectType) => typeof(CodeBlock).IsAssignableFrom(objectType);

    public void Write(RendererBase renderer, MarkdownObject objectToRender)
    {
        if (renderer is not AvaloniaRenderer avalonia || objectToRender is not CodeBlock block)
        {
            return;
        }

        var code = string.Join('\n', block.Lines.Lines?.Take(block.Lines.Count).Select(line => line.Slice.ToString()) ?? []);
        var language = block is FencedCodeBlock { Info: { Length: > 0 } info } ? info : string.Empty;
        var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Inlines = Colored(code, language), Classes = { "markdown-code" } };
        var label = new TextBlock { Text = language, IsVisible = language.Length > 0, Classes = { "code-language" } };
        var copy = new CopyButton { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Classes = { "code-copy" } };
        ToolTip.SetTip(copy, "Copy");
        AutomationProperties.SetName(copy, "Copy the code");
        copy.Command = new CopyCommand(copy, code);
        var frame = new Border { Child = new Panel { Children = { new StackPanel { Children = { label, text } }, copy } }, Classes = { "markdown-code-block" } };

        if (language.Length > 0)
        {
            frame.Classes.Add($"language-{language}");
        }

        avalonia.WriteBlock(frame);
    }

    private static string Brush(string role) => $"Syntax{char.ToUpperInvariant(role[0])}{role[1..]}Brush";

    private sealed class CopyCommand(CopyButton button, string code) : ICommand
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
                button.IsCopied = false;
                button.IsCopied = true;
            }
        }
    }
}
