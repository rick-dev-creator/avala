using Avala.Components.Privacy;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;

namespace Avala.Components.UI.Privacy;

public sealed class PersonalDetails
{
    public static readonly AttachedProperty<bool> AreHiddenProperty =
        AvaloniaProperty.RegisterAttached<PersonalDetails, Visual, bool>("AreHidden", defaultValue: true, inherits: true);

    public static readonly AttachedProperty<IPersonalText> TextProperty =
        AvaloniaProperty.RegisterAttached<PersonalDetails, TextBlock, IPersonalText>("Text", PersonalPhrase.Empty);

    public static readonly AttachedProperty<string> ScrubbedProperty =
        AvaloniaProperty.RegisterAttached<PersonalDetails, TextBlock, string>("Scrubbed", string.Empty);

    static PersonalDetails()
    {
        TextProperty.Changed.AddClassHandler<TextBlock>((block, _) => Show(block));
        ScrubbedProperty.Changed.AddClassHandler<TextBlock>((block, _) => Show(block));
        AreHiddenProperty.Changed.AddClassHandler<TextBlock>((block, _) => Show(block));
    }

    private PersonalDetails()
    {
    }

    public static IMultiValueConverter Shown { get; } = new FuncMultiValueConverter<object?, string>(values =>
    {
        var all = values.ToList();

        return all is [IPersonalText text, bool hidden] ? text.Show(hidden) : all.FirstOrDefault()?.ToString() ?? string.Empty;
    });

    public static bool GetAreHidden(Visual visual) => visual.GetValue(AreHiddenProperty);

    public static void SetAreHidden(Visual visual, bool value) => visual.SetValue(AreHiddenProperty, value);

    public static IPersonalText GetText(TextBlock block) => block.GetValue(TextProperty);

    public static void SetText(TextBlock block, IPersonalText value) => block.SetValue(TextProperty, value);

    public static string GetScrubbed(TextBlock block) => block.GetValue(ScrubbedProperty);

    public static void SetScrubbed(TextBlock block, string value) => block.SetValue(ScrubbedProperty, value);

    private static void Show(TextBlock block)
    {
        if (block.IsSet(TextProperty))
        {
            block.Text = GetText(block) is { } text ? text.Show(GetAreHidden(block)) : string.Empty;
        }
        else if (block.IsSet(ScrubbedProperty))
        {
            var text = GetScrubbed(block) ?? string.Empty;
            block.Text = GetAreHidden(block) ? PersonalMask.Scrub(text) : text;
        }
    }
}
