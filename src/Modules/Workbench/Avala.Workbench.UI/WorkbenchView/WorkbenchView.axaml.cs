using Avalonia.Controls;

namespace Avala.Workbench.UI;

internal sealed partial class WorkbenchView : UserControl
{
    public WorkbenchView()
    {
        InitializeComponent();
        HeaderFit.Attach(new HeaderParts(Header, HeaderRow, HeaderTitle, HeaderSide, JobHeaderRegion, Pill, Ava));
    }
}
