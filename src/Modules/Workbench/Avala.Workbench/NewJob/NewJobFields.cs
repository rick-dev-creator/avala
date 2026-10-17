using Avala.Workbench.RepositoryChoice;

namespace Avala.Workbench.NewJob;

internal sealed record NewJobFields(RepositoryPickerViewModel Repository, JobOptionsViewModel Options, FeatureChipViewModel Feature);
