/// <summary>
/// OeSyncDialog.xaml.cs
/// Review-and-apply dialog for the OE sync feature (Issue #44). Parses the OE Excel file,
/// diffs it against the active DB snapshot, and lets a human approve each change row by row
/// (or in bulk) before anything is written. Every apply is independent and idempotent, so
/// closing/reopening this dialog always re-detects from scratch — cancelling never leaves
/// partial state.
/// </summary>

using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DrawingTree.Data;
using DrawingTree.Logging;
using DrawingTree.Models;
using DrawingTree.Services;

using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace DrawingTree.Dialogs;

public partial class OeSyncDialog : Window
{
    private readonly OeSyncRepository _syncRepository = new();
    private readonly ObservableCollection<OeSyncChange> _changes = new();
    private bool _backupTaken;

    public OeSyncDialog()
    {
        InitializeComponent();
        ChangeList.ItemsSource = _changes;
        Loaded += OeSyncDialog_Loaded;
    }

    private async void OeSyncDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await DetectChangesAsync();
    }

    private async Task DetectChangesAsync()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        _changes.Clear();

        try
        {
            var excelPath = OeSyncConfig.GetOeExcelPath();
            List<OeSyncChange> result;
            try
            {
                result = await Task.Run(() =>
                {
                    var excelRows = OeExcelParser.Parse(excelPath);
                    var dbRows = _syncRepository.GetActiveSnapshot();
                    var existingKeys = _syncRepository.GetAllJobLineKeys();
                    return OeSyncService.ComputeDiff(excelRows, dbRows, existingKeys);
                });
            }
            catch (Exception ex)
            {
                Logger.Instance.Error($"OeSyncDialog: detection failed: {ex.Message}");
                MessageBox.Show($"Failed to read or diff the OE file:\n{ex.Message}\n\nFile path: {excelPath}",
                    "OE Sync Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            foreach (var c in result) _changes.Add(c);
            Logger.Instance.Info($"OeSyncDialog: detected {_changes.Count} pending change(s) " +
                $"(Add={result.Count(c => c.Kind == OeSyncChangeKind.Add)}, " +
                $"Modify={result.Count(c => c.Kind == OeSyncChangeKind.Modify)}, " +
                $"Deactivate={result.Count(c => c.Kind == OeSyncChangeKind.Deactivate)}, " +
                $"Anomaly={result.Count(c => c.Kind == OeSyncChangeKind.Anomaly)})");
            UpdateSummary();
        }
        finally
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateSummary()
    {
        int add = _changes.Count(c => c.Kind == OeSyncChangeKind.Add);
        int modify = _changes.Count(c => c.Kind == OeSyncChangeKind.Modify);
        int deactivate = _changes.Count(c => c.Kind == OeSyncChangeKind.Deactivate);
        int anomaly = _changes.Count(c => c.Kind == OeSyncChangeKind.Anomaly);
        int done = _changes.Count(c => c.IsSuccess);
        SummaryLabel.Text = $"Add {add} · Modify {modify} · Shipped {deactivate} · Anomaly {anomaly}   (Done {done}/{_changes.Count})";
    }

    private void EnsureBackup()
    {
        if (_backupTaken) return;
        try
        {
            DatabaseBackupService.BackupDevDatabase();
        }
        catch (Exception ex)
        {
            Logger.Instance.Error($"OeSyncDialog: backup failed: {ex.Message}");
            MessageBox.Show($"Database backup failed; this update was cancelled:\n{ex.Message}", "Backup Failed",
                MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
        _backupTaken = true;
    }

    private void ApplyOne(OeSyncChange change)
    {
        (bool Success, string? Error) result = change.Kind switch
        {
            OeSyncChangeKind.Add => _syncRepository.ApplyAdd(change),
            OeSyncChangeKind.Modify => _syncRepository.ApplyModify(change),
            OeSyncChangeKind.Deactivate => _syncRepository.ApplyDeactivate(change),
            OeSyncChangeKind.Anomaly => _syncRepository.ApplyAnomaly(change),
            _ => (false, "Unknown change kind"),
        };

        change.Status = result.Success ? OeSyncRowStatus.Success : OeSyncRowStatus.Error;
        change.ErrorMessage = result.Success ? "" : (result.Error ?? "Unknown error");
    }

    // ── Button handlers ───────────────────────────────────────────────────

    private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        bool select = SelectAllCheckBox.IsChecked == true;
        foreach (var c in _changes) c.IsSelected = select;
    }

    private void UpdateAllButton_Click(object sender, RoutedEventArgs e)
    {
        var pending = _changes.Where(c => c.IsSelected && c.Status != OeSyncRowStatus.Success).ToList();
        if (pending.Count == 0) return;

        try
        {
            EnsureBackup();
        }
        catch
        {
            return; // backup failure already reported; do not write anything
        }

        foreach (var change in pending)
            ApplyOne(change);

        UpdateSummary();
        Logger.Instance.Info($"OeSyncDialog: batch update applied to {pending.Count} row(s), " +
            $"{pending.Count(c => c.IsSuccess)} succeeded");
    }

    private void RowUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not OeSyncChange change) return;
        if (change.Status == OeSyncRowStatus.Success) return;

        try
        {
            EnsureBackup();
        }
        catch
        {
            return;
        }

        ApplyOne(change);
        UpdateSummary();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
