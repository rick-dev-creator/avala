using System.Collections.ObjectModel;
using Avala.Workbench.ModelChoices;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Avala.Workbench.Harnesses;

internal interface IHarnessChoiceViewModel
{
    bool IsShown { get; }

    string Heading { get; }

    string Note { get; }

    bool OpensUpward { get; }

    string Label { get; }

    string Monogram { get; }

    string Reason { get; }

    bool IsAttention { get; }

    bool IsOpen { get; set; }

    IReadOnlyList<IHarnessOptionViewModel> Options { get; }

    IModelPickerViewModel Models { get; }
}

internal sealed record HarnessChip(string Label, string Monogram, string Reason, bool IsAttention);

[INotifyPropertyChanged]
internal sealed partial class HarnessChoiceViewModel(string heading, bool opensUpward, IModelPickerViewModel models) : IHarnessChoiceViewModel
{
    private readonly ObservableCollection<HarnessOptionViewModel> options = [];

    public string Heading { get; } = heading;

    public bool OpensUpward { get; } = opensUpward;

    public IModelPickerViewModel Models { get; } = models;

    public IReadOnlyList<IHarnessOptionViewModel> Options => options;

    public IReadOnlyList<HarnessOptionViewModel> Offered => options;

    [ObservableProperty]
    public partial bool IsShown { get; set; }

    [ObservableProperty]
    public partial string Label { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Monogram { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Reason { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAttention { get; private set; }

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    public void Show(HarnessChip chip)
    {
        Label = chip.Label;
        Monogram = chip.Monogram;
        Reason = chip.Reason;
        IsAttention = chip.IsAttention;
    }

    public void Offer(IEnumerable<HarnessOptionViewModel> next)
    {
        options.Clear();

        foreach (var option in next)
        {
            options.Add(option);
        }
    }
}
