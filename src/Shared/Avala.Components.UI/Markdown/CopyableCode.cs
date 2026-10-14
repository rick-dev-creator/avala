using System.Globalization;
using System.Windows.Input;
using Avala.Components.Code;
using Avala.Components.UI.Copying;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Markdig.Renderers;
using Markdig.Syntax;
using MarkView.Avalonia.Extensions;
using MarkView.Avalonia.Rendering;
using MarkView.Avalonia.Rendering.Blocks;
using Path = Avalonia.Controls.Shapes.Path;

namespace Avala.Components.UI.Markdown;

public sealed class CopyableCode : IMarkViewExtension, IMarkdownObjectRenderer
{
    private const int RememberedBlocks = 256;

    private static readonly LinkedList<string> Unwrapped = [];

    public static CopyableCode Instance { get; } = new();

    public void Register(AvaloniaRenderer renderer) => renderer.ReplaceOrAdd<CodeBlockRenderer>(this);

    public bool Accept(RendererBase renderer, Type objectType) => typeof(CodeBlock).IsAssignableFrom(objectType);

    public static string Label(string language, int lines) =>
        string.Join(" · ", new[] { language, lines > 1 ? string.Create(CultureInfo.InvariantCulture, $"{lines} lines") : string.Empty }.Where(part => part.Length > 0));

    public void Write(RendererBase renderer, MarkdownObject objectToRender)
    {
        if (renderer is not AvaloniaRenderer avalonia || objectToRender is not CodeBlock block)
        {
            return;
        }

        var code = string.Join('\n', block.Lines.Lines?.Take(block.Lines.Count).Select(line => line.Slice.ToString()) ?? []);
        var language = block is FencedCodeBlock { Info: { Length: > 0 } info } ? info : string.Empty;
        var wraps = !Unwrapped.Contains(code);
        var text = new CodeText(code, language) { TextWrapping = wraps ? TextWrapping.Wrap : TextWrapping.NoWrap, Classes = { "markdown-code" } };
        var scroller = new ScrollViewer { Content = text, HorizontalScrollBarVisibility = wraps ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var label = new TextBlock { Name = "CodeLabel", Text = Label(language, block.Lines.Count), VerticalAlignment = VerticalAlignment.Center, Classes = { "code-language" } };
        var extension = CodeTokens.Extension(language);
        var wrap = new Button { Name = "WrapCode", Content = Icon("IconWrap"), Classes = { "glyph", "mini", "code-action" } };
        wrap.Classes.Set("on", wraps);
        Describe(wrap, "Wrap lines");
        wrap.Click += (_, _) => Wrap(wrap, text, scroller, code, !wrap.Classes.Contains("on"));
        var save = Action("SaveCode", "IconSave", "Save as…", new CodeRequest(CodeAction.Save, code, extension));
        var open = Action("OpenCode", "IconExternal", "Open in the editor", new CodeRequest(CodeAction.Open, code, extension));
        var copy = new CopyButton { Classes = { "code-copy", "code-action" } };
        Describe(copy, "Copy");
        AutomationProperties.SetName(copy, "Copy the code");
        copy.Command = new CopyCommand(copy, code);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, Classes = { "code-actions" }, Children = { wrap, save, open, copy } };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Classes = { "code-header" }, Children = { label, actions } };
        Grid.SetColumn(actions, 1);
        var frame = new Border { Child = new StackPanel { Children = { header, scroller } }, Classes = { "markdown-code-block" } };

        if (language.Length > 0)
        {
            frame.Classes.Add($"language-{language}");
        }

        avalonia.WriteBlock(frame);
    }

    private static Button Action(string name, string icon, string tip, CodeRequest request)
    {
        var button = new Button { Name = name, Content = Icon(icon), Classes = { "glyph", "mini", "code-action" }, IsVisible = false };
        Describe(button, tip);
        button.Command = new CodeCommand(button, request);
        button.Loaded += (_, _) => button.IsVisible = Target(button)?.CodeCommand.CanExecute(request) == true;

        return button;
    }

    private static MarkdownText? Target(Visual button) => button.FindAncestorOfType<MarkdownText>();

    private static void Describe(Control control, string tip)
    {
        ToolTip.SetTip(control, tip);
        AutomationProperties.SetName(control, tip);
    }

    private static Path Icon(string key)
    {
        var path = new Path { Classes = { "icon" } };
        path[!Path.DataProperty] = path.GetResourceObservable(key).ToBinding();

        return path;
    }

    private static void Wrap(Button wrap, CodeText text, ScrollViewer scroller, string code, bool wraps)
    {
        wrap.Classes.Set("on", wraps);
        text.TextWrapping = wraps ? TextWrapping.Wrap : TextWrapping.NoWrap;
        scroller.HorizontalScrollBarVisibility = wraps ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        Unwrapped.Remove(code);

        if (!wraps)
        {
            Unwrapped.AddLast(code);

            if (Unwrapped.Count > RememberedBlocks)
            {
                Unwrapped.RemoveFirst();
            }
        }
    }

    private sealed class CodeCommand(Button button, CodeRequest request) : ICommand
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

        public void Execute(object? parameter)
        {
            if (Target(button)?.CodeCommand is { } command && command.CanExecute(request))
            {
                command.Execute(request);
            }
        }
    }

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
