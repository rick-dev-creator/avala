using Avala.Components.Canvases;
using Avala.Components.Keycaps;
using Avala.Components.Meters;
using Avala.Components.Status;
using Avala.Sdk.UI;

namespace Avala.Components.UI;

public static class ComponentViews
{
    extension(IViewRegistrar views)
    {
        public IViewRegistrar AddComponentViews()
        {
            views.Register<IStatusDotViewModel, StatusDotView>();
            views.Register<IStatusPillViewModel, StatusPillView>();
            views.Register<IMeterViewModel, MeterView>();
            views.Register<IKeycapHintViewModel, KeycapHintView>();
            views.Register<ICanvasSurfaceViewModel, CanvasSurfaceView>();

            return views;
        }
    }
}
