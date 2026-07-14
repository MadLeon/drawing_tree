/// <summary>
/// OeSyncDialog.xaml.cs
/// Review-and-apply dialog for the OE sync feature (Issue #44). Parses the OE Excel file,
/// diffs it against the active DB snapshot, and lets a human approve each change row by row
/// (or in bulk) before anything is written. Every apply is independent and idempotent, so
/// closing/reopening this dialog always re-detects from scratch — cancelling never leaves
/// partial state.
/// </summary>

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    private ICollectionView _changesView = null!;
    private bool _backupTaken;

    private bool _showAdd = true;
    private bool _showModify = true;
    private bool _showDeactivate = true;
    private bool _showAnomaly = true;
    private bool _showHandled = false;

    public OeSyncDialog()
    {
        InitializeComponent();

        _changesView = CollectionViewSource.GetDefaultView(_changes);
        _changesView.Filter = FilterPredicate;
        _changesView.SortDescriptions.Add(new SortDescription(nameof(OeSyncChange.Kind), ListSortDirection.Ascending));
        _changesView.SortDescriptions.Add(new SortDescription(nameof(OeSyncChange.SequenceIndex), ListSortDirection.Ascending));
        ChangeList.ItemsSource = _changesView;

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
                    var allOrderItems = _syncRepository.GetAllOrderItemsByKey();
                    var handledAnomalies = _syncRepository.GetHandledAnomalies();
                    return OeSyncService.ComputeDiff(excelRows, dbRows, allOrderItems, handledAnomalies);
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

    private bool FilterPredicate(object obj)
    {
        if (obj is not OeSyncChange c) return true;
        if (c.WasPreHandled && !_showHandled) return false;
        return c.Kind switch
        {
            OeSyncChangeKind.Add => _showAdd,
            OeSyncChangeKind.Modify => _showModify,
            OeSyncChangeKind.Deactivate => _showDeactivate,
            OeSyncChangeKind.Anomaly => _showAnomaly,
            _ => true,
        };
    }

    private void UpdateSummary()
    {
        int add = _changes.Count(c => c.Kind == OeSyncChangeKind.Add);
        int modify = _changes.Count(c => c.Kind == OeSyncChangeKind.Modify);
        int deactivate = _changes.Count(c => c.Kind == OeSyncChangeKind.Deactivate);
        int anomaly = _changes.Count(c => c.Kind == OeSyncChangeKind.Anomaly);
        int done = _changes.Count(c => c.IsSuccess || c.IsIgnored);

        CategoryAddCheckBox.Content = $"Add ({add})";
        CategoryModifyCheckBox.Content = $"Modify ({modify})";
        CategoryDeactivateCheckBox.Content = $"Shipped ({deactivate})";
        CategoryAnomalyCheckBox.Content = $"Anomaly ({anomaly})";
        SummaryLabel.Text = $"Done {done}/{_changes.Count}";
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

    /// <summary>
    /// Applies a row that resolves without a modal dialog: Add/Modify/Deactivate, and Anomaly rows
    /// already resolved via the M-column-rename auto-detection or a saved OeAnomalyEditDialog edit
    /// (EffectiveKind routes those to Add/Modify). A plain unresolved Anomaly must go through
    /// OeAnomalyEditDialog instead — see the NeedsEditDialog guard in RowUpdateButton_Click/
    /// UpdateAllButton_Click that keeps such rows out of this method entirely.
    /// </summary>
    private void ApplyOne(OeSyncChange change)
    {
        bool wasCorrectedAnomaly = change.Kind == OeSyncChangeKind.Anomaly &&
            (change.WasResolvedViaEditForm || change.IsLineNumberRenameCandidate);
        bool success;
        string? error;

        switch (change.EffectiveKind)
        {
            case OeSyncChangeKind.Add:
                var addResult = _syncRepository.ApplyAdd(change);
                success = addResult.Success;
                error = addResult.Error;
                if (success) change.CreatedOrderItemId = addResult.NewOrderItemId;
                break;
            case OeSyncChangeKind.Modify:
                var modResult = _syncRepository.ApplyModify(change, change.NeedsItemReactivation, change.NeedsPoReactivation);
                success = modResult.Success;
                error = modResult.Error;
                break;
            case OeSyncChangeKind.Deactivate:
                // DeactivatesPo = false is a partial shipment (see OeSyncChange.DeactivatesPo) —
                // ApplyAnomaly already does exactly "deactivate this one order_item, never touch
                // its PO", so it's reused here instead of a separate single-item Deactivate path.
                (success, error) = change.DeactivatesPo
                    ? _syncRepository.ApplyDeactivate(change)
                    : _syncRepository.ApplyAnomaly(change);
                break;
            default:
                // A plain Anomaly (not a rename candidate, not already edited) must be resolved
                // through OeAnomalyEditDialog — see RowUpdateButton_Click.
                success = false;
                error = "This anomaly must be resolved through the edit dialog";
                break;
        }

        change.Status = success ? OeSyncRowStatus.Success : OeSyncRowStatus.Error;
        change.ErrorMessage = success ? "" : (error ?? "Unknown error");

        if (success && wasCorrectedAnomaly)
            _syncRepository.RecordAnomalyHandled(change, "corrected");
        else if (success && change.WasPreHandled)
            // The reviewer overrode a past ignore/correction and actually applied this Modify/Add —
            // the stale memory record no longer describes reality, so drop it rather than leave it
            // pointing at a fingerprint that (now that this is applied) can never recur anyway.
            _syncRepository.DeleteHandledAnomaly(change.HandledJobNumber, change.HandledLineNumber);
    }

    // ── Button handlers ───────────────────────────────────────────────────

    private void SelectAllCheckBox_Click(object sender, RoutedEventArgs e)
    {
        bool select = SelectAllCheckBox.IsChecked == true;
        foreach (var c in _changesView.Cast<OeSyncChange>()) c.IsSelected = select;
    }

    private void ShowHandledCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _showHandled = ShowHandledCheckBox.IsChecked == true;
        _changesView.Refresh();
    }

    private void CategoryFilterCheckBox_Click(object sender, RoutedEventArgs e)
    {
        _showAdd = CategoryAddCheckBox.IsChecked == true;
        _showModify = CategoryModifyCheckBox.IsChecked == true;
        _showDeactivate = CategoryDeactivateCheckBox.IsChecked == true;
        _showAnomaly = CategoryAnomalyCheckBox.IsChecked == true;
        _changesView.Refresh();
    }

    /// <summary>True for a plain Anomaly row (not an M-column-rename candidate, not already resolved)
    /// that can only be resolved through OeAnomalyEditDialog and so cannot be batch-applied.</summary>
    private static bool NeedsEditDialog(OeSyncChange change)
        => change.Kind == OeSyncChangeKind.Anomaly && change.EffectiveKind == OeSyncChangeKind.Anomaly;

    private void UpdateAllButton_Click(object sender, RoutedEventArgs e)
    {
        // A natural-key match to an archived order_item (NeedsItemReactivation/NeedsPoReactivation)
        // is a normal Modify — reactivation is automatic per business rule, not a judgment call —
        // so it's intentionally NOT excluded here and applies via bulk Update-All like any other row.
        var pending = _changesView.Cast<OeSyncChange>()
            .Where(c => c.IsSelected && c.CanUpdate && !NeedsEditDialog(c))
            .ToList();
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
        if (!change.CanUpdate) return;

        try
        {
            EnsureBackup();
        }
        catch
        {
            return;
        }

        if (NeedsEditDialog(change))
        {
            var dialog = new OeAnomalyEditDialog(_syncRepository, change) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                if (!dialog.MarkedShipped)
                {
                    change.WasResolvedViaEditForm = true;
                    _syncRepository.RecordAnomalyHandled(change, "corrected");
                }
                change.Status = OeSyncRowStatus.Success;
                change.ErrorMessage = "";
                UpdateSummary();
            }
            return;
        }

        ApplyOne(change);
        UpdateSummary();
    }

    private void RowUndoButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not OeSyncChange change) return;
        if (!change.CanUndo) return;

        bool wasCorrectedAnomaly = change.Kind == OeSyncChangeKind.Anomaly &&
            (change.WasResolvedViaEditForm || change.IsLineNumberRenameCandidate);

        try
        {
            EnsureBackup();
        }
        catch
        {
            return;
        }

        (bool Success, string? Error) result = change.EffectiveKind switch
        {
            OeSyncChangeKind.Add => _syncRepository.UndoAdd(change),
            OeSyncChangeKind.Modify => _syncRepository.UndoModify(change),
            OeSyncChangeKind.Deactivate => change.DeactivatesPo
                ? _syncRepository.UndoDeactivate(change)
                : _syncRepository.UndoAnomaly(change),
            _ => _syncRepository.UndoAnomaly(change),
        };

        if (result.Success)
        {
            change.Status = OeSyncRowStatus.None;
            change.ErrorMessage = "";
            change.CreatedOrderItemId = null;
            change.WasResolvedViaEditForm = false;
            change.WasReactivated = false;
            change.PoWasReactivated = false;

            // The "corrected" memory record this row wrote on Update no longer reflects reality
            // once the correction itself is undone.
            if (wasCorrectedAnomaly)
                _syncRepository.DeleteHandledAnomaly(change.HandledJobNumber, change.HandledLineNumber);
        }
        else
        {
            MessageBox.Show($"Undo failed:\n{result.Error}", "Undo Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        UpdateSummary();
    }

    private void RowIgnoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.DataContext is not OeSyncChange change) return;
        if (!change.CanIgnore) return;

        change.Status = OeSyncRowStatus.Ignored;

        // Persisted so the same proposed value doesn't keep re-nagging every sync run (matched by
        // Fingerprint, not just job/line — see OeSyncService.ComputeFingerprint). Add stays
        // session-only: its fingerprint would just be "the whole new row," which isn't a
        // meaningful thing to remember ignoring (an ignored Add is either handled by Anomaly's
        // "already exists" path, or the reviewer will just re-ignore the genuinely-new row next
        // time anyway).
        if (change.Kind != OeSyncChangeKind.Add)
            _syncRepository.RecordAnomalyHandled(change, "ignored");

        UpdateSummary();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
