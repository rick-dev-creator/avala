using Avala.Sdk;
using Avala.Sdk.Regions;
using Avala.Sdk.UI;
using Avala.Workbench.Notes;
using Microsoft.Extensions.DependencyInjection;

namespace Avala.Workbench.UI;

internal static class NotesRegistration
{
    public static void Register(IPluginRegistrar registrar)
    {
        registrar.Services
            .AddTransient<NoteBoardViewModel>()
            .AddSingleton<NotesToggleViewModel>()
            .AddSingleton<NotesPaneViewModel>()
            .AddSingleton<ProjectNotesViewModel>()
            .AddForwarded<IPage, ProjectNotesViewModel>();
        registrar
            .AddToRegion<NotesToggleViewModel>(WorkbenchRegions.JobHeader, 7)
            .AddToRegion<NotesPaneViewModel>(WorkbenchRegions.Preview, 30);
    }

    public static void RegisterViews(IViewRegistrar views)
    {
        views.Register<INotesToggleViewModel, NotesToggleView>();
        views.Register<INotesPaneViewModel, NotesPaneView>();
        views.Register<INoteBoardViewModel, NoteBoardView>();
        views.Register<INoteRowViewModel, NoteRowView>();
        views.Register<INoteImageViewModel, NoteImageView>();
        views.Register<IProjectNotesViewModel, ProjectNotesView>();
    }
}
