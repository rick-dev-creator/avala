using System.Collections.ObjectModel;
using Avala.Documentation.Contracts;
using Avala.Jobs.Contracts;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.Workbench.NewJob;

internal interface IFeatureChipViewModel
{
    bool IsShown { get; }

    string Name { get; }

    string Fact { get; }

    bool IsOpen { get; set; }

    IReadOnlyList<IFeatureOptionViewModel> Options { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FeatureChipViewModel(IDocumentation documentation) : IFeatureChipViewModel
{
    private readonly ObservableCollection<FeatureOptionViewModel> options = [];
    private IReadOnlyList<RepositoryFeature> features = [];
    private string instruction = string.Empty;
    private FeatureChoice chosen = FeatureChoice.Undocumented;
    private bool picked;
    private int reads;

    public IReadOnlyList<IFeatureOptionViewModel> Options => options;

    public IReadOnlyList<FeatureOptionViewModel> Offered => options;

    public FeatureChoice Chosen => chosen;

    [ObservableProperty]
    public partial bool IsShown { get; private set; }

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Fact { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    public async Task ReadAsync(string repository, CancellationToken cancellationToken)
    {
        var ticket = ++reads;
        var docs = string.IsNullOrWhiteSpace(repository) ? RepositoryDocs.Off : await documentation.OfRepositoryAsync(repository.Trim(), cancellationToken);

        if (ticket == reads)
        {
            features = docs.Features;
            IsShown = docs.Enabled;
            picked = false;
            Show();
        }
    }

    public void Follow(string next)
    {
        instruction = next;

        if (!picked)
        {
            Show();
        }
    }

    public async Task KeepAsync(JobId job, CancellationToken cancellationToken)
    {
        if (IsShown)
        {
            await documentation.ChooseAsync(job, chosen, cancellationToken);
        }

        picked = false;
    }

    private void Show()
    {
        var suggestion = documentation.Suggest(instruction, [.. features.Select(listed => listed.Feature)]);

        if (!picked)
        {
            chosen = FeatureChoice.Of(suggestion.Feature);
        }

        options.Clear();

        if (suggestion.IsNew)
        {
            options.Add(new FeatureOptionViewModel(FeatureChoice.Of(suggestion.Feature), FeaturePhrases.Suggested, Choose));
        }

        foreach (var listed in features)
        {
            options.Add(new FeatureOptionViewModel(FeatureChoice.Of(listed.Feature), FeaturePhrases.Versions(listed.Versions), Choose));
        }

        options.Add(new FeatureOptionViewModel(FeatureChoice.Undocumented, string.Empty, Choose));

        foreach (var option in options)
        {
            option.Mark(option.Choice == chosen);
        }

        Name = chosen.Feature.Match(feature => feature.Title, () => FeaturePhrases.Undocumented);
        Fact = chosen.Feature.Match(
            feature => features.FirstOrDefault(listed => listed.Feature.Slug == feature.Slug) is { } existing ? FeaturePhrases.Next(existing.Versions) : FeaturePhrases.New,
            () => string.Empty);
    }

    private void Choose(FeatureChoice choice)
    {
        chosen = choice;
        picked = true;
        IsOpen = false;
        Show();
    }
}

internal interface IFeatureOptionViewModel
{
    string Title { get; }

    string Fact { get; }

    bool IsNew { get; }

    bool IsNone { get; }

    bool IsChosen { get; }

    IRelayCommand ChooseCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class FeatureOptionViewModel(FeatureChoice choice, string fact, Action<FeatureChoice> choose) : IFeatureOptionViewModel
{
    public FeatureChoice Choice { get; } = choice;

    public string Title { get; } = choice.Feature.Match(feature => feature.Title, () => FeaturePhrases.DontDocument);

    public string Fact { get; } = fact;

    public bool IsNew { get; } = fact == FeaturePhrases.Suggested;

    public bool IsNone => Choice.Feature.IsNone;

    [ObservableProperty]
    public partial bool IsChosen { get; private set; }

    public void Mark(bool chosen) => IsChosen = chosen;

    [RelayCommand]
    private void Choose() => choose(Choice);
}

internal static class FeaturePhrases
{
    public const string Heading = "Feature";

    public const string Suggested = "New · suggested from your instruction";

    public const string New = "new";

    public const string Undocumented = "Not documented";

    public const string DontDocument = "Don't document this job";

    public static string Versions(int count) => count == 1 ? "1 version" : $"{count} versions";

    public static string Next(int versions) => $"v{versions + 1} next";
}
