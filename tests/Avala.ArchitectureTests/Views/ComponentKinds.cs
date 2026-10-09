using Avalonia.Controls;

namespace Avala.ArchitectureTests.Views;

internal static class ComponentKinds
{
    public const string ViewModelSuffix = "ViewModel";
    public const string ViewSuffix = "View";
    public const string DesignPrefix = "Design";
    public const string ScriptsSuffix = "Scripts";

    extension(Type type)
    {
        public bool IsViewModel =>
            type is { IsClass: true } && type.Name.EndsWith(ViewModelSuffix, StringComparison.Ordinal) && !type.IsDesignViewModel;

        public bool IsDesignViewModel =>
            type is { IsClass: true }
            && type.Name.StartsWith(DesignPrefix, StringComparison.Ordinal)
            && type.Name.EndsWith(ViewModelSuffix, StringComparison.Ordinal);

        public bool IsViewModelInterface =>
            type is { IsInterface: true }
            && type.Name.StartsWith('I')
            && type.Name.EndsWith(ViewModelSuffix, StringComparison.Ordinal);

        public bool IsView => typeof(Control).IsAssignableFrom(type) && type.Name.EndsWith(ViewSuffix, StringComparison.Ordinal);

        public string Stem => type.IsView ? type.Name[..^ViewSuffix.Length] : type.Name[..^ViewModelSuffix.Length];
    }
}
