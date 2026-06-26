/// <summary>
/// LoadingOverlayControl.xaml.cs
/// Reusable spinning-arc loading overlay. Place inside a Grid with Panel.ZIndex="10",
/// start as Visibility="Collapsed", and toggle Visibility to show/hide.
/// </summary>

using UserControl = System.Windows.Controls.UserControl;

namespace DrawingTree.Controls;

public partial class LoadingOverlayControl : UserControl
{
    public LoadingOverlayControl()
    {
        InitializeComponent();
    }
}
