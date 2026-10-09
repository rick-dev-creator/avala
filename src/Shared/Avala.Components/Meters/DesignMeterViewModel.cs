namespace Avala.Components.Meters;

public sealed class DesignMeterViewModel : IMeterViewModel
{
    public string Label => "claude-work · 5h";

    public double Fraction => 0.88;

    public string Reading => "88% · resets 16:20";

    public bool HasThreshold => true;

    public double Threshold => 0.9;

    public bool IsNearThreshold => true;
}
