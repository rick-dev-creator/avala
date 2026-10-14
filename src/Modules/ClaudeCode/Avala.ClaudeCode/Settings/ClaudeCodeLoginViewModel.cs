using Avala.Agents.Contracts.Connections;
using Avala.ClaudeCode.Conversations;
using Avala.Sdk;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Avala.ClaudeCode.Settings;

internal interface IClaudeCodeLoginViewModel
{
    string Name { get; }

    string Where { get; }

    bool IsSmall { get; }

    bool IsStandard { get; }

    bool IsLarge { get; }

    bool IsFull { get; }

    string Custom { get; }

    bool HasCustom { get; }

    bool AutoCompact { get; }

    bool IdleCompaction { get; }

    string Summary { get; }

    string Problem { get; }

    bool HasProblem { get; }

    IAsyncRelayCommand<CompactChoice> ChooseCommand { get; }

    IAsyncRelayCommand RetryCommand { get; }

    IAsyncRelayCommand ToggleAutoCompactCommand { get; }

    IAsyncRelayCommand ToggleIdleCompactionCommand { get; }
}

[INotifyPropertyChanged]
internal sealed partial class ClaudeCodeLoginViewModel : IClaudeCodeLoginViewModel
{
    private readonly DeclaredConnection connection;
    private readonly IConnections connections;
    private IReadOnlyDictionary<string, Option<string>> last = new Dictionary<string, Option<string>>();

    public ClaudeCodeLoginViewModel(DeclaredConnection connection, IConnections connections)
    {
        this.connection = connection;
        this.connections = connections;
        Name = connection.Name.Value;
        Where = connection.Reference.Match(reference => reference, () => connection.Origin == ConnectionOrigin.Implicit ? "signed in with claude" : "API key");
        Tokens = CommandLine.CompactAt(new ConnectionEnvironment { Settings = connection.Settings });
        AutoCompact = Flag(CommandLine.AutoCompactSetting);
        IdleCompaction = Flag(CommandLine.IdleCompactionSetting);
        Problem = string.Empty;
    }

    public string Name { get; }

    public string Where { get; }

    public bool IsSmall => Choice == CompactChoice.Small;

    public bool IsStandard => Choice == CompactChoice.Standard;

    public bool IsLarge => Choice == CompactChoice.Large;

    public bool IsFull => Choice == CompactChoice.Full;

    public string Custom => HasCustom ? $"{Tokens / 1000}k in connections.json" : string.Empty;

    public bool HasCustom => CompactWindows.Of(Tokens).IsNone;

    public string Summary => CompactWindows.Summary(Tokens, AutoCompact);

    public bool HasProblem => Problem.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSmall), nameof(IsStandard), nameof(IsLarge), nameof(IsFull), nameof(Custom), nameof(HasCustom), nameof(Summary))]
    public partial int Tokens { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyCanExecuteChangedFor(nameof(ToggleIdleCompactionCommand))]
    public partial bool AutoCompact { get; private set; }

    [ObservableProperty]
    public partial bool IdleCompaction { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProblem))]
    public partial string Problem { get; private set; }

    private CompactChoice? Choice => CompactWindows.Of(Tokens).Match<CompactChoice?>(choice => choice, () => null);

    [RelayCommand]
    private async Task ChooseAsync(CompactChoice choice, CancellationToken cancellationToken)
    {
        Tokens = CompactWindows.Tokens(choice);
        await SaveAsync(Change(CommandLine.CompactAtSetting, choice == CompactChoice.Standard ? Option<string>.None : Tokens.ToString(System.Globalization.CultureInfo.InvariantCulture)), cancellationToken);
    }

    [RelayCommand]
    private Task RetryAsync(CancellationToken cancellationToken) => SaveAsync(last, cancellationToken);

    [RelayCommand]
    private Task ToggleAutoCompactAsync(CancellationToken cancellationToken)
    {
        AutoCompact = !AutoCompact;

        return SaveAsync(Switched(CommandLine.AutoCompactSetting, AutoCompact), cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(AutoCompact))]
    private Task ToggleIdleCompactionAsync(CancellationToken cancellationToken)
    {
        IdleCompaction = !IdleCompaction;

        return SaveAsync(Switched(CommandLine.IdleCompactionSetting, IdleCompaction), cancellationToken);
    }

    private static Dictionary<string, Option<string>> Switched(string setting, bool on) => Change(setting, on ? Option<string>.None : "false");

    private async Task SaveAsync(IReadOnlyDictionary<string, Option<string>> settings, CancellationToken cancellationToken)
    {
        last = settings;
        var edit = new ConnectionEdit(connection.Name, connection.Provider)
        {
            Credential = connection.Source.Bind(source => connection.Reference.Map(reference => new CredentialReference(source, reference))),
            Settings = settings,
        };
        var saved = await connections.DeclareAsync(connection.Origin == ConnectionOrigin.Declared ? connection.Name : Option<ConnectionName>.None, edit, cancellationToken);
        Problem = saved.Match(_ => string.Empty, error => $"Could not save this setting: {error}");
    }

    private static Dictionary<string, Option<string>> Change(string setting, Option<string> value) => new() { [setting] = value };

    private bool Flag(string setting) =>
        !connection.Settings.TryGetValue(setting, out var value) || !bool.TryParse(value, out var on) || on;
}
