/// <summary>
/// SearchControl.xaml.cs
/// Search screen for finding parts by drawing number.
/// Supports debounced live search and navigation to DrawingViewerControl.
/// </summary>

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DrawingTree.Data;
using DrawingTree.Logging;

using UserControl = System.Windows.Controls.UserControl;
using Button      = System.Windows.Controls.Button;

namespace DrawingTree.Controls;

public partial class SearchControl : UserControl
{
    private readonly DrawingRepository _repository = new();
    private readonly DispatcherTimer _debounceTimer;

    /// <summary>Fired when the user clicks View on a search result row. Argument is the part.id.</summary>
    public event EventHandler<int>? NavigateToPartRequested;

    public SearchControl()
    {
        InitializeComponent();

        _debounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            RunSearch(SearchBox.Text.Trim());
        };
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PlaceholderText.Visibility = SearchBox.Text.Length == 0
            ? Visibility.Visible : Visibility.Collapsed;
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void RunSearch(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            ResultsGrid.ItemsSource = null;
            return;
        }

        Logger.Instance.Info($"SearchControl: searching for '{query}'");
        var results = _repository.SearchPartsWithJobContext(query);
        ResultsGrid.ItemsSource = results;
        Logger.Instance.Info($"SearchControl: {results.Count} result(s) found");
    }

    private void ViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int partId)
        {
            Logger.Instance.Info($"SearchControl: navigate to partId={partId}");
            NavigateToPartRequested?.Invoke(this, partId);
        }
    }
}
