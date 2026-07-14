using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DrawingTree.Models;

/// <summary>
/// Classification of one OE sync change, matching the four outcomes of the diff engine.
/// </summary>
public enum OeSyncChangeKind { Add, Modify, Deactivate, Anomaly }

/// <summary>Apply result for one OeSyncChange row, mirrored by the review dialog's status icon.</summary>
public enum OeSyncRowStatus { None, Success, Error, Ignored }

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

    /// <summary>
    /// Settable (not init-only) so OeAnomalyEditDialog can attach a freshly-built row — built from
    /// the reviewer's edited field values — to an Anomaly change that had no ExcelRow to begin with
    /// (e.g. a "sibling rows missing" anomaly, DbRow-only), before handing off to ApplyAdd/ApplyModify.
    /// </summary>
    public OeExcelRow? ExcelRow { get; set; }

    /// <summary>
    /// Settable for the same reason as ExcelRow: OeAnomalyEditDialog may discover — only once the
    /// reviewer confirms the edit — that a "Job # + M already exists" Anomaly (originally detected
    /// with DbRow null, since GetActiveSnapshot excludes inactive rows) actually collides with an
    /// existing, inactive order_item. The dialog attaches that row here so the save routes through
    /// ApplyModify (reactivating it) instead of ApplyAdd, which would otherwise violate
    /// UNIQUE(order_item.job_id, order_item.line_number).
    /// </summary>
    public OeDbRow? DbRow { get; set; }

    /// <summary>Add/Modify target identifiers (Modify/Deactivate only; null for Add before insert).
    /// Settable so OeAnomalyEditDialog can attach the ids of a reactivated order_item alongside its
    /// DbRow — see DbRow's doc comment.</summary>
    public long? OrderItemId { get; set; }
    public long? PoId { get; set; }

    /// <summary>Per-field old/new pairs (Modify kind only).</summary>
    public List<OeFieldChange> FieldChanges { get; init; } = new();

    /// <summary>True when this row carries at least one field diff to show in the review dialog (Modify rows, and some Anomaly rows).</summary>
    public bool HasFieldChanges => FieldChanges.Count > 0;

    /// <summary>Other Excel rows under the same PO, shown for side-by-side review (Anomaly kind only).</summary>
    public List<OeExcelRow> SiblingExcelRows { get; init; } = new();

    /// <summary>
    /// The order_item(s) this Deactivate-kind change marks inactive. When <see cref="DeactivatesPo"/>
    /// is true this is every item under the owning PO (a full "PO shipped" group). When false it's a
    /// single item — a partial shipment: this row is missing from the OE file, but sibling rows
    /// under the same PO are still present, so the PO itself stays active (see ComputeDiff's
    /// unmatchedByPo handling) and only this one item is marked shipped.
    /// </summary>
    public List<OeDbRow> DeactivateItems { get; init; } = new();

    /// <summary>
    /// True (the default) for a full "every order_item under this PO is missing from the OE file"
    /// group — Apply/UndoDeactivate flip purchase_order.is_active along with every DeactivateItems
    /// entry. False for a single-item partial shipment (sibling rows under the same PO are still
    /// active, so the PO must stay active) — ApplyOne routes that case through ApplyAnomaly/
    /// UndoAnomaly instead, which only ever touches the one order_item named by OrderItemId.
    /// </summary>
    public bool DeactivatesPo { get; init; } = true;

    /// <summary>Display text for the Deactivate row panel — distinguishes a full PO group from a
    /// single-item partial shipment (DeactivatesPo false), since the two mean different things.</summary>
    public string DeactivateSummary => DeactivatesPo
        ? "Shipped — all rows under this PO are no longer in the OE file"
        : "Shipped — this row is missing from the OE file; other rows under the same PO are still active";

    /// <summary>Why this row is an anomaly (parser warning text, or the cross-check reason).</summary>
    public string AnomalyReason { get; init; } = "";

    /// <summary>
    /// True when this Anomaly pairs one newly-added-looking Excel row with one apparently-missing DB
    /// row under the same job_number, differing only by line_number — a likely M-column rename rather
    /// than two independent events. See OeSyncService.ComputeDiff's rename-candidate pass.
    /// </summary>
    public bool IsLineNumberRenameCandidate { get; init; }

    /// <summary>
    /// True when this Modify's target order_item (or its owning PO) was archived (is_active = 0)
    /// before this natural-key match — a purchase_order's active status is fully derived from its
    /// order_items, so reactivating a natural-key match is automatic, not a judgment call (see
    /// OeSyncService.ComputeDiff's allOrderItems lookup). Passed straight into ApplyModify's
    /// reactivateItem/reactivatePo parameters by OeSyncDialog.ApplyOne, applied and undone exactly
    /// like any other Modify field — including via bulk Update-All.
    /// </summary>
    public bool NeedsItemReactivation { get; init; }
    public bool NeedsPoReactivation { get; init; }

    /// <summary>
    /// True once this Anomaly's data was hand-corrected and saved via OeAnomalyEditDialog (Issue #44
    /// follow-up). Drives EffectiveKind so Undo routes to UndoAdd/UndoModify instead of UndoAnomaly.
    /// </summary>
    public bool WasResolvedViaEditForm { get; set; }

    /// <summary>
    /// True when ApplyModify's reactivate path flipped this DbRow's order_item.is_active from 0 to
    /// 1 (a "Job # + M already exists but inactive" Anomaly, saved via OeAnomalyEditDialog). Undo
    /// uses this to flip it back to 0 — an ordinary Modify never touches is_active, so this stays
    /// false for every other row.
    /// </summary>
    public bool WasReactivated { get; set; }

    /// <summary>Same as <see cref="WasReactivated"/>, for the owning purchase_order — set only when
    /// the PO itself was inactive too and had to be reactivated for this one item to come back.</summary>
    public bool PoWasReactivated { get; set; }

    /// <summary>
    /// order_item.id created by a successful Add apply (including a corrected Anomaly that
    /// resolves to Add). Populated post-apply so RowUndoButton_Click can delete it again.
    /// </summary>
    public long? CreatedOrderItemId { get; set; }

    /// <summary>
    /// purchase_order.contact_id as it was before ApplyModify touched Customer/Contact fields.
    /// Populated only when that branch runs, so UndoModify can restore it.
    /// </summary>
    public long? PreviousContactId { get; set; }

    /// <summary>Position of this row in the diff engine's output, used as a stable sort tiebreaker
    /// once the review dialog groups rows by Kind instead of showing raw detection order.</summary>
    public int SequenceIndex { get; set; }

    /// <summary>
    /// Hash of the specific field value(s) this Modify/Anomaly change proposes, computed once at
    /// diff time by OeSyncService.ComputeFingerprint. Compared against oe_anomaly_handled to decide
    /// whether this is literally the same change a past sync run already ignored/corrected, or a
    /// genuinely new one (Excel value changed, or a new field started differing).
    /// </summary>
    public string Fingerprint { get; init; } = "";

    /// <summary>
    /// Normalized (job_number, line_number) this change was detected under, frozen at diff time —
    /// the oe_anomaly_handled write/delete key. NOT re-derived from ExcelRow/DbRow at write time,
    /// because OeAnomalyEditDialog replaces ExcelRow with the reviewer's edited values before
    /// ApplyAdd/ApplyModify runs; deriving the key from the post-edit ExcelRow would write under
    /// whatever line number the reviewer just typed — which can collide with and clobber a
    /// completely unrelated order_item's own memory row at that same (job, line) key (e.g. a blank-M
    /// Anomaly hand-assigned "M1" colliding with an already-ignored Deactivate whose real order_item
    /// happens to sit at job/M1).
    /// </summary>
    public string HandledJobNumber { get; init; } = "";
    public string HandledLineNumber { get; init; } = "";

    /// <summary>
    /// True when Fingerprint matched a past "handled" (ignored or corrected) memory record — this
    /// row is pre-set to Ignored status (see AddAnomaly/TryAddModify) so it doesn't force the
    /// reviewer to re-decide the same thing every sync run. Still visible via the dialog's
    /// "Show Handled" filter, and Update remains available as an "actually, apply it" override.
    /// </summary>
    public bool WasPreHandled { get; init; }

    /// <summary>
    /// When set, this same (job_number, line_number) + change was already reviewed in a past OE
    /// sync run — see oe_anomaly_handled / OeSyncRepository.GetHandledAnomalies.
    /// </summary>
    public DateTime? HandledAt { get; init; }

    /// <summary>"ignored" or "corrected" — how the change named by <see cref="HandledAt"/> was resolved.</summary>
    public string? HandledAction { get; init; }

    /// <summary>Display text for the annotation shown on a previously-reviewed row; empty when unhandled.</summary>
    public string HandledSummary => HandledAt.HasValue
        ? $"Already handled ({HandledAction}) on {HandledAt.Value:yyyy-MM-dd}"
        : "";

    /// <summary>
    /// Kind this row should actually be applied/undone as. An Anomaly the reviewer has resolved
    /// (via the M-column-rename auto-detection, or by hand-editing and saving through
    /// OeAnomalyEditDialog) is really an Add (no DbRow yet) or a Modify (DbRow exists) once its
    /// data is complete — this lets ApplyOne/UndoOne reuse the Add/Modify code paths instead of a
    /// separate "apply a corrected anomaly" branch.
    /// </summary>
    public OeSyncChangeKind EffectiveKind =>
        Kind == OeSyncChangeKind.Anomaly &&
            (IsLineNumberRenameCandidate || WasResolvedViaEditForm)
            ? (DbRow == null ? OeSyncChangeKind.Add : OeSyncChangeKind.Modify)
            : Kind;

    private bool _isSelected;
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
                OnPropertyChanged(nameof(IsIgnored));
                OnPropertyChanged(nameof(CanUpdate));
                OnPropertyChanged(nameof(CanUndo));
                OnPropertyChanged(nameof(CanIgnore));
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
    public bool IsIgnored => _status == OeSyncRowStatus.Ignored;

    /// <summary>Update is available until the row is applied — including on an Ignored row (whether
    /// ignored this session or pre-handled from a past sync), as an "actually, apply it" override.</summary>
    public bool CanUpdate => !IsSuccess;

    /// <summary>Undo is only meaningful once this row has actually written to the database.</summary>
    public bool CanUndo => IsSuccess;

    /// <summary>Ignore is available for any unresolved row.</summary>
    public bool CanIgnore => !IsSuccess && !IsIgnored;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
