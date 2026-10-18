using Avala.Sdk.UI;
using Avala.Workbench.Conversation;

namespace Avala.Workbench.UI;

internal static class MediaRegistration
{
    public static void RegisterViews(IViewRegistrar views)
    {
        views.Register<IMediaViewModel, MediaView>();
        views.Register<IMediaViewerViewModel, MediaViewerView>();
    }
}
