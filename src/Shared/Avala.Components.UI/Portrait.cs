using Avala.Components.Status;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Avala.Components.UI;

public sealed class Portrait : TemplatedControl
{
    public static readonly StyledProperty<Mood> MoodProperty =
        AvaloniaProperty.Register<Portrait, Mood>(nameof(Mood));

    public static readonly DirectProperty<Portrait, Bitmap> PictureProperty =
        AvaloniaProperty.RegisterDirect<Portrait, Bitmap>(nameof(Picture), portrait => portrait.Picture);

    private static readonly Dictionary<Mood, Bitmap> Pictures = [];

    private Bitmap picture;

    public Portrait()
    {
        picture = PictureFor(Mood);
        Show(Mood);
    }

    public Mood Mood
    {
        get => GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    public Bitmap Picture
    {
        get => picture;
        private set => SetAndRaise(PictureProperty, ref picture, value);
    }

    public static Uri PictureOf(Mood mood) => new($"avares://Avala.Components.UI/Assets/Ava/{mood.ToString().ToLowerInvariant()}.png");

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MoodProperty)
        {
            var mood = change.GetNewValue<Mood>();
            Picture = PictureFor(mood);
            Show(mood);
        }
    }

    private static Bitmap PictureFor(Mood mood)
    {
        if (!Pictures.TryGetValue(mood, out var found))
        {
            using var stream = AssetLoader.Open(PictureOf(mood));
            found = new Bitmap(stream);
            Pictures[mood] = found;
        }

        return found;
    }

    private void Show(Mood mood)
    {
        PseudoClasses.Set(":focused", mood == Mood.Focused);
        PseudoClasses.Set(":asking", mood == Mood.Asking);
        PseudoClasses.Set(":worried", mood == Mood.Worried);
        PseudoClasses.Set(":pleased", mood == Mood.Pleased);
    }
}
