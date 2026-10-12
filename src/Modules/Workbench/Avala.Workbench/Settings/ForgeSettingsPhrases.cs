using System.Globalization;
using Avala.Forges.Contracts;
using Avala.Workbench.Presenting;

namespace Avala.Workbench.Settings;

internal static class ForgeSettingsPhrases
{
    public static string File(ForgeCatalog catalog) =>
        catalog.FileError.Match(
            error => $"forges.json is rejected: {ForgePhrases.Error(error)}",
            () => string.Create(CultureInfo.InvariantCulture, $"forges.json · checks every {catalog.PollInterval.TotalSeconds:0}s"));
}
