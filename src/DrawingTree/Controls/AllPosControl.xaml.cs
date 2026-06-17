/// <summary>
/// AllPosControl.xaml.cs
/// Lists every order_item line across all purchase orders.
/// Loads all rows on construction (no search/filter) and navigates to the
/// single-PO page when the user clicks View on a row.
/// </summary>

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using DrawingTree.Data;
using DrawingTree.Logging;

using UserControl = System.Windows.Controls.UserControl;
using Button      = System.Windows.Controls.Button;

namespace DrawingTree.Controls;

public partial class AllPosControl : UserControl
{
    private readonly PoRepository _repository = new();

    /// <summary>Fired when the user clicks View on a PO line. Argument is purchase_order.id.</summary>
    public event EventHandler<int>? NavigateToPoRequested;

    public AllPosControl()
    {
        InitializeComponent();
        LoadAllPos();
    }

    private void LoadAllPos()
    {
        var results = _repository.GetAllPoLines();

        var view = new ListCollectionView(results);
        view.SortDescriptions.Add(new SortDescription(nameof(PoListRow.OeNumber),   ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(PoListRow.LineNumber),  ListSortDirection.Ascending));
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PoListRow.PoNumber)));

        ResultsGrid.ItemsSource = view;
        Logger.Instance.Info($"AllPosControl: loaded {results.Count} PO line(s)");
    }

    private void ViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int poId)
        {
            Logger.Instance.Info($"AllPosControl: navigate to poId={poId}");
            NavigateToPoRequested?.Invoke(this, poId);
        }
    }
}
