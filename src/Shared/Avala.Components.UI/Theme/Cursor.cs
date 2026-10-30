using Avala.Sdk;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Avala.Components.UI.Theme;

public sealed class Cursor
{
    public static readonly AttachedProperty<bool> SlidesProperty =
        AvaloniaProperty.RegisterAttached<Cursor, SelectingItemsControl, bool>("Slides");

    public static readonly TimeSpan Glide = TimeSpan.FromSeconds(0.32);

    private static readonly AttachedProperty<Border?> MarkProperty =
        AvaloniaProperty.RegisterAttached<Cursor, SelectingItemsControl, Border?>("Mark");

    private static readonly AttachedProperty<object?> PlacedProperty =
        AvaloniaProperty.RegisterAttached<Cursor, SelectingItemsControl, object?>("Placed");

    private static readonly AttachedProperty<double> TargetProperty =
        AvaloniaProperty.RegisterAttached<Cursor, SelectingItemsControl, double>("Target");

    private static readonly SplineEasing Spring = new(0.34, 1.4, 0.64, 1);

    static Cursor() => SlidesProperty.Changed.AddClassHandler<SelectingItemsControl>((list, _) => Watch(list));

    private Cursor()
    {
    }

    public static bool GetSlides(SelectingItemsControl list) => list.GetValue(SlidesProperty);

    public static void SetSlides(SelectingItemsControl list, bool value) => list.SetValue(SlidesProperty, value);

    internal static Option<Border> MarkOf(SelectingItemsControl list) => list.GetValue(MarkProperty).ToOption();

    private static void Watch(SelectingItemsControl list)
    {
        list.LayoutUpdated -= Relayout;
        AdornerLayer.SetAdorner(list, null);
        list.ClearValue(MarkProperty);

        if (!GetSlides(list))
        {
            return;
        }

        var mark = new Border
        {
            Classes = { "cursor" },
            IsHitTestVisible = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            RenderTransform = new TranslateTransform(),
            IsVisible = false,
        };
        list.SetValue(MarkProperty, mark);
        AdornerLayer.SetAdorner(list, new Canvas { IsHitTestVisible = false, Children = { mark } });
        list.LayoutUpdated += Relayout;
    }

    private static void Relayout(object? sender, EventArgs e)
    {
        if (sender is SelectingItemsControl list && list.GetValue(MarkProperty) is { } mark)
        {
            Place(list, mark);
        }
    }

    private static void Place(SelectingItemsControl list, Border mark)
    {
        if (!list.IsEffectivelyVisible || list.ContainerFromIndex(list.SelectedIndex) is not { } chosen || chosen.TranslatePoint(default, list) is not { } at)
        {
            mark.IsVisible = false;
            list.ClearValue(PlacedProperty);
            return;
        }

        var moving = (TranslateTransform)mark.RenderTransform!;
        var chose = !Equals(list.GetValue(PlacedProperty), list.SelectedItem);
        var moved = Math.Abs(list.GetValue(TargetProperty) - at.Y) > 0.5;
        list.SetValue(PlacedProperty, list.SelectedItem);
        list.SetValue(TargetProperty, at.Y);

        if (!chose && !moved && mark.IsVisible)
        {
            return;
        }

        if (chose && mark.IsVisible && !Motion.GetIsReduced(list))
        {
            moving.Transitions ??= [new DoubleTransition { Property = TranslateTransform.YProperty, Duration = Glide, Easing = Spring }];
        }
        else
        {
            moving.Transitions = null;
        }

        mark.Width = chosen.Bounds.Width;
        mark.Height = chosen.Bounds.Height;
        Canvas.SetLeft(mark, at.X);
        moving.Y = at.Y;
        mark.IsVisible = true;
    }
}
