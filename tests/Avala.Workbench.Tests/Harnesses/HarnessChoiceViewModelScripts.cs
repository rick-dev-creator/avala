using Avala.Agents.Contracts.Connections;
using Avala.Sdk;
using Avala.Workbench.Harnesses;
using Avala.Workbench.ModelChoices;

namespace Avala.Workbench.Tests.Harnesses;

public sealed class HarnessChoiceViewModelScripts
{
    [Fact]
    public void TheChipShowsWhatItIsToldUnderItsOwnHeadingAndOpeningSide()
    {
        var chip = new HarnessChoiceViewModel("Connection", opensUpward: true, new ModelPickerViewModel());

        chip.Show(new HarnessChip("claude-work · opus · high", "CW", "Runs on claude-work", IsAttention: true));

        Assert.Equal(("Connection", true, "claude-work · opus · high", "CW", "Runs on claude-work", true), (chip.Heading, chip.OpensUpward, chip.Label, chip.Monogram, chip.Reason, chip.IsAttention));
    }

    [Fact]
    public void OfferingOptionsReplacesTheMenusOptions()
    {
        var chip = new HarnessChoiceViewModel("Run on", opensUpward: false, new ModelPickerViewModel());
        chip.Offer([new HarnessOptionViewModel("Auto", Option<ConnectionName>.None, _ => { })]);

        chip.Offer([new HarnessOptionViewModel("claude-work", new ConnectionName("claude-work"), _ => { }), new HarnessOptionViewModel("Continue on claude-personal", new ConnectionName("claude-personal"), _ => { })]);

        Assert.Equal(["claude-work", "Continue on claude-personal"], chip.Options.Select(option => option.Name));
    }
}
