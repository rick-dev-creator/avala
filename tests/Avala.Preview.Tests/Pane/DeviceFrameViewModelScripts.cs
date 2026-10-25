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

    [Fact]
    public void TurningTheDeviceSwapsItsWidthAndHeight()
    {
        var frame = new DeviceFrameViewModel();
        Choose(frame, "Tablet");

        frame.TurnCommand.Execute(null);

        Assert.Equal((1180d, 820d, "Tablet · 1180 × 820"), (frame.FrameWidth, frame.FrameHeight, frame.Label));
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

        Assert.Equal((false, double.PositiveInfinity), (frame.IsFramed, frame.FrameWidth));
        Assert.False(frame.TurnCommand.CanExecute(null));
    }

    private static void Choose(DeviceFrameViewModel frame, string name) =>
        frame.Choices.Single(choice => choice.Name == name).Choose.Execute(null);
}
