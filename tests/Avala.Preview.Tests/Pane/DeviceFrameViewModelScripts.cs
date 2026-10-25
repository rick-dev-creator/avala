using Avala.Preview.Pane;

namespace Avala.Preview.Tests.Pane;

public sealed class DeviceFrameViewModelScripts
{
    [Fact]
    public void ThePageFillsThePaneUntilADeviceIsChosen()
    {
        var frame = new DeviceFrameViewModel();

        Assert.Equal((false, double.PositiveInfinity, double.PositiveInfinity, "Responsive"), (frame.IsFramed, frame.FrameWidth, frame.FrameHeight, frame.Label));
        Assert.False(frame.TurnCommand.CanExecute(null));
        Assert.Equal(["Responsive"], frame.Choices.Where(choice => choice.IsChosen).Select(choice => choice.Name));
    }

    [Fact]
    public void ChoosingADeviceFramesThePageAtItsSize()
    {
        var frame = new DeviceFrameViewModel();

        Choose(frame, "iPhone");

        Assert.Equal((true, 393d, 852d, "iPhone · 393 × 852"), (frame.IsFramed, frame.FrameWidth, frame.FrameHeight, frame.Label));
        Assert.Equal(["iPhone"], frame.Choices.Where(choice => choice.IsChosen).Select(choice => choice.Name));
    }

    [Theory]
    [InlineData("iPhone", 12, true, false, false)]
    [InlineData("Android", 10, false, true, false)]
    [InlineData("Tablet", 20, false, false, true)]
    public void ADeviceIsFramedByABezelOfItsShape(string name, double bezel, bool phone, bool android, bool tablet)
    {
        var frame = new DeviceFrameViewModel();

        Choose(frame, name);

        Assert.Equal((bezel, frame.FrameWidth + (2 * bezel), frame.FrameHeight + (2 * bezel)), (frame.Bezel, frame.OuterWidth, frame.OuterHeight));
        Assert.Equal((phone, android, tablet), (frame.IsPhone, frame.IsAndroid, frame.IsTablet));
    }

    [Fact]
    public void TurningTheDeviceSwapsItsWidthAndHeight()
    {
        var frame = new DeviceFrameViewModel();
        Choose(frame, "Tablet");

        frame.TurnCommand.Execute(null);

        Assert.Equal((1180d, 820d, "Tablet · 1180 × 820", true), (frame.FrameWidth, frame.FrameHeight, frame.Label, frame.IsTurned));
    }

    [Fact]
    public void AnotherDeviceStartsUpright()
    {
        var frame = new DeviceFrameViewModel();
        Choose(frame, "iPhone");
        frame.TurnCommand.Execute(null);

        Choose(frame, "Android");

        Assert.Equal((412d, 915d), (frame.FrameWidth, frame.FrameHeight));
    }

    [Fact]
    public void ResponsiveLetsThePageFillThePaneAgain()
    {
        var frame = new DeviceFrameViewModel();
        Choose(frame, "iPhone");

        Choose(frame, "Responsive");

        Assert.Equal((false, double.PositiveInfinity, 0d), (frame.IsFramed, frame.FrameWidth, frame.Bezel));
        Assert.False(frame.TurnCommand.CanExecute(null));
    }

    private static void Choose(DeviceFrameViewModel frame, string name) =>
        frame.Choices.Single(choice => choice.Name == name).Choose.Execute(null);
}
