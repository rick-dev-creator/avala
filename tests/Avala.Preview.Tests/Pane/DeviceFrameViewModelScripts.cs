using Avala.Preview.Navigation;
using Avala.Preview.Pane;
using Avala.Sdk;

namespace Avala.Preview.Tests.Pane;

public sealed class DeviceFrameViewModelScripts
{
    [Fact]
    public void ThePageFillsThePaneUntilADeviceIsChosen()
    {
        var frame = new DeviceFrameViewModel();

        Assert.Equal((false, double.PositiveInfinity, double.PositiveInfinity, "Responsive"), (frame.IsFramed, frame.FrameWidth, frame.FrameHeight, frame.Label));
        Assert.False(frame.TurnCommand.CanExecute(null));
        Assert.False(frame.ToggleDepthCommand.CanExecute(null));
        Assert.Equal(["Responsive"], frame.Choices.Where(choice => choice.IsChosen).Select(choice => choice.Name));
    }

    [Fact]
    public void TheMenuOffersSixDevicesAfterResponsive() =>
        Assert.Equal(
            ["Responsive", "iPhone SE", "iPhone 16", "iPhone 16 Pro Max", "Pixel 9", "Surface Duo", "iPad Air 11\""],
            new DeviceFrameViewModel().Choices.Select(choice => choice.Name));

    [Fact]
    public void ChoosingADeviceFramesThePageAtItsSize()
    {
        var frame = new DeviceFrameViewModel();

        Choose(frame, "iPhone 16");

        Assert.Equal((true, 393d, 852d, "iPhone 16 · 393 × 852"), (frame.IsFramed, frame.FrameWidth, frame.FrameHeight, frame.Label));
        Assert.Equal(["iPhone 16"], frame.Choices.Where(choice => choice.IsChosen).Select(choice => choice.Name));
    }

    [Theory]
    [InlineData("iPhone SE", 24, 140, "classic")]
    [InlineData("iPhone 16", 24, 24, "island")]
    [InlineData("Pixel 9", 20, 20, "punch")]
    [InlineData("Surface Duo", 28, 80, "dual")]
    [InlineData("iPad Air 11\"", 40, 40, "tablet")]
    public void ADeviceIsFramedByTheBezelsOfItsShape(string name, double across, double along, string shape)
    {
        var frame = new DeviceFrameViewModel();

        Choose(frame, name);

        Assert.Equal((frame.FrameWidth + across, frame.FrameHeight + along), (frame.OuterWidth, frame.OuterHeight));
        Assert.Equal(shape, Shape(frame));
    }

    [Fact]
    public void TurningTheDeviceSwapsItsSidesAndItsBezels()
    {
        var frame = new DeviceFrameViewModel();
        Choose(frame, "iPhone SE");

        frame.TurnCommand.Execute(null);

        Assert.Equal((667d, 375d, "iPhone SE · 667 × 375", true), (frame.FrameWidth, frame.FrameHeight, frame.Label, frame.IsTurned));
        Assert.Equal(new DeviceInsets(70, 12, 70, 12), frame.Insets);
    }

    [Fact]
    public void AnotherDeviceStartsUpright()
    {
        var frame = new DeviceFrameViewModel();
        Choose(frame, "iPhone 16");
        frame.TurnCommand.Execute(null);

        Choose(frame, "Pixel 9");

        Assert.Equal((412d, 915d), (frame.FrameWidth, frame.FrameHeight));
    }

    [Fact]
    public void ResponsiveLetsThePageFillThePaneAgain()
    {
        var frame = new DeviceFrameViewModel();
        Choose(frame, "iPhone 16");

        Choose(frame, "Responsive");

        Assert.Equal((false, double.PositiveInfinity, DeviceInsets.None), (frame.IsFramed, frame.FrameWidth, frame.Insets));
        Assert.False(frame.TurnCommand.CanExecute(null));
    }

    [Fact]
    public void ThreeDimensionsShowTheDeviceAsAModelInsteadOfAFlatFrame()
    {
        var frame = new DeviceFrameViewModel();
        Choose(frame, "iPhone 16 Pro Max");

        frame.ToggleDepthCommand.Execute(null);

        Assert.Equal((true, false, double.PositiveInfinity), (frame.ShowsDepth, frame.IsFlat, frame.OuterWidth));
        Assert.Equal(string.Empty, Shape(frame));
        Assert.Equal(Option<DeviceView>.Some(new DeviceView("iphone-16-pro-max", 440, 956, false)), frame.Depth);
    }

    [Fact]
    public void ATurnedDeviceInThreeDimensionsNamesItsTurnedSize()
    {
        var frame = new DeviceFrameViewModel();
        Choose(frame, "Surface Duo");
        frame.ToggleDepthCommand.Execute(null);

        frame.TurnCommand.Execute(null);

        Assert.Equal(Option<DeviceView>.Some(new DeviceView("surface-duo", 720, 1114, true)), frame.Depth);
    }

    [Fact]
    public void ResponsiveLeavesThreeDimensions()
    {
        var frame = new DeviceFrameViewModel();
        Choose(frame, "Pixel 9");
        frame.ToggleDepthCommand.Execute(null);

        Choose(frame, "Responsive");
        Choose(frame, "Pixel 9");

        Assert.Equal((false, true), (frame.ShowsDepth, frame.IsFlat));
    }

    [Fact]
    public void EveryChangeIsAnnounced()
    {
        var frame = new DeviceFrameViewModel();
        var changes = 0;
        frame.Changed += (_, _) => changes++;

        Choose(frame, "iPhone 16");
        frame.TurnCommand.Execute(null);
        frame.ToggleDepthCommand.Execute(null);

        Assert.Equal(3, changes);
    }

    private static string Shape(DeviceFrameViewModel frame) =>
        frame.IsIsland ? "island" : frame.IsPunch ? "punch" : frame.IsClassic ? "classic" : frame.IsDual ? "dual" : frame.IsTablet ? "tablet" : string.Empty;

    private static void Choose(DeviceFrameViewModel frame, string name) =>
        frame.Choices.Single(choice => choice.Name == name).Choose.Execute(null);
}
