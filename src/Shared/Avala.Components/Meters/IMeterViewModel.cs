namespace Avala.Components.Meters;

public interface IMeterViewModel
{
    string Label { get; }

    double Fraction { get; }

    string Reading { get; }

    bool HasThreshold { get; }

    double Threshold { get; }

    bool IsNearThreshold { get; }
}
