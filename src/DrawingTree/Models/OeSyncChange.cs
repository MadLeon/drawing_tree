using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DrawingTree.Models;

/// <summary>
/// Classification of one OE sync change, matching the four outcomes of the diff engine.
/// </summary>
public enum OeSyncChangeKind { Add, Modify, Deactivate, Anomaly }

/// <summary>Apply result for one OeSyncChange row, mirrored by the review dialog's status icon.</summary>
public enum OeSyncRowStatus { None, Success, Error }

/// <summary>One field that differs between the Excel row and the matched DB row (Modify kind only).</summary>
public class OeFieldChange
{
    public string FieldLabel { get; init; } = "";
    public string OldValue { get; init; } = "";
    public string NewValue { get; init; } = "";
}

/// <summary>
/// A single pending OE sync change surfaced to the human reviewer. Produced by
/// OeSyncService.ComputeDiff and consumed by the review dialog.
/// </summary>
public class OeSyncChange : INotifyPropertyChanged
{
    public OeSyncChangeKind Kind { get; init; }

    /// <summary>Short display line (job#/part#/line) shown at the top of the row.</summary>
    public string HeaderText { get; init; } = "";

    public OeExcelRow? ExcelRow { get; init; }
    public OeDbRow? DbRow { get; init; }

    /// <summary>Add/Modify target identifiers (Modify/Deactivate only; null for Add before insert).</summary>
    public long? OrderItemId { get; init; }
    public long? PoId { get; init; }

    /// <summary>Per-field old/new pairs (Modify kind only).</summary>
    public List<OeFieldChange> FieldChanges { get; init; } = new();

    /// <summary>Other Excel rows under the same PO, shown for side-by-side review (Anomaly kind only).</summary>
    public List<OeExcelRow> SiblingExcelRows { get; init; } = new();

    /// <summary>
    /// All order_items under the owning PO (Deactivate kind only). Applying this change sets
    /// purchase_order.is_active = 0 and order_item.is_active = 0 for every item here, atomically.
    /// </summary>
    public List<OeDbRow> DeactivateItems { get; init; } = new();

    /// <summary>Why this row is an anomaly (parser warning text, or the cross-check reason).</summary>
    public string AnomalyReason { get; init; } = "";

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    private OeSyncRowStatus _status = OeSyncRowStatus.None;
    public OeSyncRowStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsSuccess));
                OnPropertyChanged(nameof(IsError));
            }
        }
    }

    private string _errorMessage = "";
    public string ErrorMessage
    {
        get => _errorMessage;
        set { if (_errorMessage != value) { _errorMessage = value; OnPropertyChanged(); } }
    }

    public bool IsSuccess => _status == OeSyncRowStatus.Success;
    public bool IsError => _status == OeSyncRowStatus.Error;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
