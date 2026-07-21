/// <summary>
/// DirLogDialog.xaml.cs
/// Modal window host for <see cref="Controls.DirLogControl"/>, so the DIR Log is shown
/// as a popup instead of taking over the main display area.
/// </summary>

using System.Windows;

namespace DrawingTree.Dialogs;

public partial class DirLogDialog : Window
{
    public DirLogDialog()
    {
        InitializeComponent();
    }
}
