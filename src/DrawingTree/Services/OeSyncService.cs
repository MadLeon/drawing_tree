/// <summary>
/// OeSyncService.cs
/// Pure diff engine for the OE sync feature (Issue #44): compares the current OE Excel content
/// ("set A") against the DB snapshot of active order_items ("set B") and classifies every
/// difference as Add / Modify / Deactivate / Anomaly. No file or database access here — this
/// class only transforms data already read by OeExcelParser / OeSyncRepository.
/// </summary>

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DrawingTree.Models;

namespace DrawingTree.Services;

public static class OeSyncService
{
    /// <summary>
    /// Hashes the specific field value(s) a Modify/Anomaly change proposes, so oe_anomaly_handled
    /// can recognize "the reviewer already ignored/corrected exactly this" versus "this looks like
    /// the same (job, line) but the actual proposed value changed since." Deliberately excludes old
    /// values — the reviewer's decision was about the incoming value, not what it's replacing.
    /// </summary>
    internal static string ComputeFingerprint(string kind, IEnumerable<string?> parts)
    {
        var joined = kind + "\x1F" + string.Join("\x1F", parts.Select(p => p ?? ""));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(hash);
    }

    /// <summary>Fingerprint for a Modify row: the field labels + proposed new values, order-independent.</summary>
    private static string ComputeModifyFingerprint(List<OeFieldChange> fieldChanges)
        => ComputeFingerprint("Modify", fieldChanges
            .OrderBy(f => f.FieldLabel, StringComparer.Ordinal)
            .Select(f => $"{f.FieldLabel}={f.NewValue}"));

    /// <summary>
    /// Fingerprint for an Anomaly row: the reason plus a full snapshot of whichever row triggered it
    /// (Excel row when present, else the DB row). Not the FieldChanges list — those get cleared out
    /// once a correctable anomaly is resolved, which would otherwise make "corrected" never match.
    /// </summary>
    private static string ComputeAnomalyFingerprint(string reason, OeExcelRow? excelRow, OeDbRow? dbRow)
    {
        if (excelRow != null)
            return ComputeFingerprint("Anomaly", new[]
            {
                reason, excelRow.OeNumber, excelRow.JobNumber, excelRow.Customer, excelRow.Quantity,
                excelRow.PartNumber, excelRow.Revision, excelRow.Contact, excelRow.DrawingReleaseDate,
                excelRow.LineNumber, excelRow.Description, excelRow.Price?.ToString(CultureInfo.InvariantCulture),
                excelRow.PoNumber, excelRow.DeliveryRequiredDate,
            });

        return ComputeFingerprint("Anomaly", new[]
        {
            reason, dbRow?.PoNumber, dbRow?.OeNumber, dbRow?.CustomerName, dbRow?.Quantity,
            dbRow?.DrawingNumber, dbRow?.Revision, dbRow?.ContactName, dbRow?.DrawingReleaseDate,
            dbRow?.LineNumber, dbRow?.Description, dbRow?.ActualPrice?.ToString(CultureInfo.InvariantCulture),
            dbRow?.DeliveryRequiredDate,
        });
    }

    /// <summary>
    /// Fingerprint for a Deactivate row: there's no old/new value pair to hash (the "proposal" is
    /// just "mark this set of order_items inactive") — so this hashes the group's *membership*
    /// instead: the PO number plus every item's (job_number, line_number), order-independent. Same
    /// members next sync -> same fingerprint -> stays suppressed. A new item joins the group (one
    /// more row vanished from Excel) or one leaves (reappeared) -> different membership -> different
    /// fingerprint -> resurfaces for review, exactly when the situation actually changed.
    /// </summary>
    internal static string ComputeDeactivateFingerprint(List<OeDbRow> items)
        => ComputeFingerprint("Deactivate", new[] { items[0].PoNumber.Trim().ToUpperInvariant() }
            .Concat(items
                .Select(DbMatchKeyOf)
                .OrderBy(k => k.Job, StringComparer.Ordinal).ThenBy(k => k.Line, StringComparer.Ordinal)
                .Select(k => $"{k.Job}\x1F{k.Line}")));

    /// <summary>
    /// Builds one Deactivate change for <paramref name="items"/> (a full PO-shipped group when
    /// <paramref name="deactivatesPo"/> is true, or a single item when false — see
    /// OeSyncChange.DeactivatesPo) with the same fingerprint-based "already handled" suppression
    /// Modify/Anomaly rows get. Every item's own memory row must match the fresh fingerprint for the
    /// group to stay suppressed — if even one doesn't (a fresh item, or a stale/partial memory
    /// write), membership has changed since it was ignored, so the whole thing resurfaces as new.
    /// </summary>
    private static void AddDeactivate(List<OeSyncChange> changes, List<OeDbRow> items, bool deactivatesPo,
        string headerText, IReadOnlyDictionary<(string Job, string Line), OeAnomalyHandled> handledAnomalies)
    {
        var fingerprint = ComputeDeactivateFingerprint(items);

        DateTime? handledAt = null;
        string? handledAction = null;
        bool wasPreHandled = items.All(d =>
            handledAnomalies.TryGetValue(DbMatchKeyOf(d), out var h) && h.Fingerprint == fingerprint);
        if (wasPreHandled && handledAnomalies.TryGetValue(DbMatchKeyOf(items[0]), out var handled))
        {
            handledAt = handled.HandledAt;
            handledAction = handled.Action;
        }

        var groupKey = DbMatchKeyOf(items[0]);
        changes.Add(new OeSyncChange
        {
            Kind = OeSyncChangeKind.Deactivate,
            HeaderText = headerText,
            DbRow = items[0],
            OrderItemId = items[0].OrderItemId,
            PoId = items[0].PoId,
            DeactivateItems = items,
            DeactivatesPo = deactivatesPo,
            Fingerprint = fingerprint,
            HandledJobNumber = groupKey.Job,
            HandledLineNumber = groupKey.Line,
            WasPreHandled = wasPreHandled,
            Status = wasPreHandled ? OeSyncRowStatus.Ignored : OeSyncRowStatus.None,
            HandledAt = handledAt,
            HandledAction = handledAction,
        });
    }

    /// <summary>Computes every pending OE sync change between the Excel rows and the DB snapshot.</summary>
    /// <param name="excelRows">Rows parsed from the OE Excel file ("set A").</param>
    /// <param name="dbRows">Active order_items under active POs ("set B").</param>
    /// <param name="allOrderItems">
    /// Every order_item in the database keyed by (job_number, line_number), regardless of active
    /// status, with each item's/PO's active flag. A would-be Add whose key collides here is a
    /// historical/archived row reappearing in the append-only Excel log, not a genuinely new item —
    /// inserting it would violate UNIQUE(job_id, line_number). A purchase_order's active status is
    /// fully derived from its order_items, so this is folded straight into a normal Modify with
    /// reactivation (NeedsItemReactivation/NeedsPoReactivation) rather than a separate Anomaly
    /// review step. Pass null/empty to skip this check (e.g. in isolated unit tests).
    /// </param>
    /// <param name="handledAnomalies">
    /// (job_number, line_number) -> the last "handled" (ignored/corrected) memory record for that
    /// key, from oe_anomaly_handled. When a freshly-detected anomaly's key AND reason text match a
    /// record here, the resulting OeSyncChange is annotated (HandledAt/HandledAction) instead of
    /// silently suppressed — the row still needs review, but the reviewer is told it was already
    /// looked at once. Pass null/empty to skip annotation (e.g. in isolated unit tests).
    /// </param>
    public static List<OeSyncChange> ComputeDiff(List<OeExcelRow> excelRows, List<OeDbRow> dbRows,
        IReadOnlyDictionary<(string Job, string Line), (OeDbRow Row, bool ItemInactive, bool PoInactive)>? allOrderItems = null,
        IReadOnlyDictionary<(string Job, string Line), OeAnomalyHandled>? handledAnomalies = null)
    {
        allOrderItems ??= new Dictionary<(string, string), (OeDbRow, bool, bool)>();
        handledAnomalies ??= new Dictionary<(string, string), OeAnomalyHandled>();
        var changes = new List<OeSyncChange>();

        var warningRows = excelRows.Where(r => r.ParseWarnings.Count > 0).ToList();
        var candidateRows = excelRows.Where(r => r.ParseWarnings.Count == 0).ToList();

        foreach (var r in warningRows)
            AddAnomaly(changes, r, null, string.Join("; ", r.ParseWarnings), handledAnomalies);

        var excelGroups = candidateRows.GroupBy(MatchKeyOf).ToList();
        var excelByKey = new Dictionary<(string Job, string Line), OeExcelRow>();
        foreach (var g in excelGroups)
        {
            if (g.Key.Job.Length == 0)
            {
                foreach (var r in g) AddAnomaly(changes, r, null, "Job # is blank, cannot locate the corresponding order", handledAnomalies);
                continue;
            }
            if (g.Count() > 1)
            {
                foreach (var r in g)
                    AddAnomaly(changes, r, null, $"The same Job # + M column appears {g.Count()} times, cannot determine a unique match", handledAnomalies);
                continue;
            }
            excelByKey[g.Key] = g.Single();
        }

        var dbByKey = new Dictionary<(string Job, string Line), OeDbRow>();
        foreach (var d in dbRows)
            dbByKey[DbMatchKeyOf(d)] = d; // job_number + line_number is unique per schema

        var matchedDbKeys = new HashSet<(string, string)>();
        var pendingAddRows = new List<(OeExcelRow Row, (string Job, string Line) Key)>();

        foreach (var (key, excelRow) in excelByKey)
        {
            if (dbByKey.TryGetValue(key, out var dbRow))
            {
                matchedDbKeys.Add(key);
                TryAddModify(changes, excelRow, dbRow, handledAnomalies);
            }
            else if (allOrderItems.TryGetValue(key, out var archived))
            {
                // Natural-key (job_number, line_number) match to an order_item that exists but is
                // archived (its own is_active = 0, or its PO's is), or whose PO is archived while
                // the item itself is active — a purchase_order's active status is fully derived
                // from whether any of its order_items are active, so reactivating this one is
                // automatic, not a judgment call: fold it into a normal Modify (reactivateItem/
                // reactivatePo) so Update/Update-All apply it exactly like any other field change,
                // with no separate Anomaly review step.
                TryAddModify(changes, excelRow, archived.Row, handledAnomalies,
                    reactivateItem: archived.ItemInactive, reactivatePo: archived.PoInactive);
            }
            else
            {
                pendingAddRows.Add((excelRow, key));
            }
        }

        // A row that fails full-diff eligibility (a parse warning, e.g. an unparseable Price cell)
        // or collides with a sibling under the same Job#+M (duplicate-key anomaly) is excluded from
        // excelByKey above, but it is still physically present in the OE file — its own anomaly is
        // already raised separately. Without this, the matching DB row would wrongly look "missing
        // from the OE file" and get misclassified as shipped/Deactivate below.
        var allExcelPresenceKeys = excelRows
            .Select(MatchKeyOf)
            .Where(k => k.Job.Length > 0 && k.Line.Length > 0)
            .ToHashSet();
        foreach (var d in dbRows)
        {
            var key = DbMatchKeyOf(d);
            if (allExcelPresenceKeys.Contains(key))
                matchedDbKeys.Add(key);
        }

        // A rare but real case: the user renumbers the M column for an existing job in the OE file
        // (e.g. "1" -> "01A"). The old (job, line) key vanishes and a new one appears, so naive matching
        // sees "one DB row missing" + "one brand-new Excel row" instead of one renamed item. When exactly
        // one still-unmatched Excel row and exactly one still-unmatched DB row share the same job_number,
        // treat it as a single rename candidate — surfaced as one Anomaly (not auto Add + auto Deactivate)
        // carrying the same field diff as a normal Modify, plus the M-column change itself. Any messier
        // case (a job with several lines changing at once) is intentionally left to the existing
        // Add/Deactivate/Anomaly paths below — pairing would be a guess, and guessing isn't allowed.
        var unmatchedDbRowsForRename = dbRows.Where(d => !matchedDbKeys.Contains(DbMatchKeyOf(d))).ToList();
        var addCandidatesByJob = pendingAddRows.GroupBy(p => p.Key.Job).ToDictionary(g => g.Key, g => g.ToList());
        var missingByJob = unmatchedDbRowsForRename.GroupBy(d => DbMatchKeyOf(d).Job).ToDictionary(g => g.Key, g => g.ToList());
        var consumedAddKeys = new HashSet<(string Job, string Line)>();

        foreach (var (job, addCandidates) in addCandidatesByJob)
        {
            if (job.Length == 0) continue; // blank job already handled as its own anomaly above
            if (addCandidates.Count != 1) continue;
            if (!missingByJob.TryGetValue(job, out var missingCandidates) || missingCandidates.Count != 1) continue;

            var excelRow = addCandidates[0].Row;
            var dbRow = missingCandidates[0];

            var fieldChanges = BuildNonQuantityFieldChanges(excelRow, dbRow);
            fieldChanges.Add(new OeFieldChange { FieldLabel = "M (line #):", OldValue = dbRow.LineNumber, NewValue = excelRow.LineNumber });

            AddAnomaly(changes, excelRow, dbRow,
                $"M column may have changed from \"{dbRow.LineNumber}\" to \"{excelRow.LineNumber}\" for this job — confirm this is a line-number rename, not a shipped item plus a new one",
                handledAnomalies, fieldChanges: fieldChanges, isLineNumberRenameCandidate: true);

            consumedAddKeys.Add(addCandidates[0].Key);
            matchedDbKeys.Add(DbMatchKeyOf(dbRow));
        }

        foreach (var (row, key) in pendingAddRows)
        {
            if (consumedAddKeys.Contains(key)) continue;
            TryAddAdd(changes, row, handledAnomalies);
        }

        var matchedPoIds = dbRows.Where(d => matchedDbKeys.Contains(DbMatchKeyOf(d))).Select(d => d.PoId).ToHashSet();
        var unmatchedByPo = dbRows.Where(d => !matchedDbKeys.Contains(DbMatchKeyOf(d))).GroupBy(d => d.PoId);

        foreach (var group in unmatchedByPo)
        {
            var items = group.ToList();
            if (matchedPoIds.Contains(group.Key))
            {
                // A purchase_order's active status is fully derived from its order_items: other
                // rows under this PO are still in the OE file, so the PO stays active — but these
                // specific items are missing, i.e. a partial shipment. Not ambiguous, so not an
                // Anomaly: mark just these items shipped (one Deactivate row each), PO untouched.
                foreach (var d in items)
                    AddDeactivate(changes, new List<OeDbRow> { d }, deactivatesPo: false,
                        $"Job #{d.JobNumber} / M{d.LineNumber} / {d.DrawingNumber} — missing from the OE file; " +
                        $"other rows under PO {d.PoNumber} are still active (shipped)",
                        handledAnomalies);
            }
            else
            {
                AddDeactivate(changes, items, deactivatesPo: true,
                    $"PO {items[0].PoNumber} (Job #{FormatJobNumberRange(items)}) — " +
                    $"{items.Count} row(s) missing from the OE file (shipped)",
                    handledAnomalies);
            }
        }

        for (int i = 0; i < changes.Count; i++)
            changes[i].SequenceIndex = i;

        return changes;
    }

    /// <summary>
    /// Summarizes the job numbers of a Deactivate group as "min–max" when every job_number is
    /// numeric, or a distinct sorted list otherwise (job numbers are not always purely numeric).
    /// </summary>
    private static string FormatJobNumberRange(List<OeDbRow> items)
    {
        var distinct = items.Select(d => d.JobNumber).Distinct().ToList();

        if (distinct.All(j => long.TryParse(j, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            var numeric = distinct.Select(long.Parse).OrderBy(n => n).ToList();
            return numeric[0] == numeric[^1] ? numeric[0].ToString() : $"{numeric[0]}–{numeric[^1]}";
        }

        var sorted = distinct.OrderBy(j => j, StringComparer.OrdinalIgnoreCase).ToList();
        return sorted.Count == 1 ? sorted[0] : string.Join(", ", sorted);
    }

    private static (string Job, string Line) MatchKeyOf(OeExcelRow r)
        => (OeNormalization.NormalizeJobNumber(r.JobNumber), OeNormalization.NormalizeLineNumber(r.LineNumber));

    private static (string Job, string Line) DbMatchKeyOf(OeDbRow d)
        => (OeNormalization.NormalizeJobNumber(d.JobNumber), OeNormalization.NormalizeLineNumber(d.LineNumber));

    /// <summary>internal so OeAnomalyEditDialog can reuse the same field-diff logic when the reviewer
    /// hand-corrects an Anomaly row's data.</summary>
    internal static List<OeFieldChange> BuildNonQuantityFieldChanges(OeExcelRow excelRow, OeDbRow dbRow)
    {
        var fieldChanges = new List<OeFieldChange>();

        if (!PricesEqual(excelRow.Price, dbRow.ActualPrice))
            fieldChanges.Add(new OeFieldChange { FieldLabel = "Price:", OldValue = FormatPrice(dbRow.ActualPrice), NewValue = FormatPrice(excelRow.Price) });

        if (!TextEquals(excelRow.DrawingReleaseDate, dbRow.DrawingReleaseDate ?? ""))
            fieldChanges.Add(new OeFieldChange { FieldLabel = "DWG Rel.", OldValue = dbRow.DrawingReleaseDate ?? "", NewValue = excelRow.DrawingReleaseDate });

        if (!TextEquals(excelRow.DeliveryRequiredDate, dbRow.DeliveryRequiredDate ?? ""))
            fieldChanges.Add(new OeFieldChange { FieldLabel = "Del. Req'd:", OldValue = dbRow.DeliveryRequiredDate ?? "", NewValue = excelRow.DeliveryRequiredDate });

        if (!TextEqualsCI(excelRow.PartNumber, dbRow.DrawingNumber))
            fieldChanges.Add(new OeFieldChange { FieldLabel = "Part #", OldValue = dbRow.DrawingNumber, NewValue = excelRow.PartNumber });

        // A blank Excel revision never clobbers a DB value that's already populated.
        bool revIsClobber = string.IsNullOrWhiteSpace(excelRow.Revision) && !string.IsNullOrWhiteSpace(dbRow.Revision);
        if (!revIsClobber && !TextEqualsCI(excelRow.Revision, dbRow.Revision))
            fieldChanges.Add(new OeFieldChange { FieldLabel = "Rev", OldValue = dbRow.Revision, NewValue = excelRow.Revision });

        if (!TextEquals(excelRow.Description, dbRow.Description ?? ""))
            fieldChanges.Add(new OeFieldChange { FieldLabel = "Descriptions:", OldValue = dbRow.Description ?? "", NewValue = excelRow.Description });

        if (!TextEqualsCI(excelRow.OeNumber, dbRow.OeNumber ?? ""))
            fieldChanges.Add(new OeFieldChange { FieldLabel = "O.E.:", OldValue = dbRow.OeNumber ?? "", NewValue = excelRow.OeNumber });

        // A blank/"NPO"-marker Excel PO cell paired with our own auto-generated "NPO-" placeholder
        // (any era's format) is not a real edit — the OE file never carried a real PO number for
        // this job — so it must never be flagged as an attempt to clear the PO number back to blank.
        bool poIsAutoNpo = OeNormalization.IsAutoGeneratedNpo(excelRow.PoNumber, dbRow.PoNumber);
        if (!poIsAutoNpo && !OeNormalization.ArePoNumbersEquivalent(excelRow.PoNumber, dbRow.PoNumber))
        {
            // The proposed value shown/applied is the resolved placeholder ("NPO-{job}"), not raw
            // Excel text — some OE rows type the literal word "NPO" into the P.O. column instead of
            // leaving it blank (e.g. Job #72113/#72942), and that must never be taken as a literal
            // PO number.
            var newPoNumber = OeNormalization.ResolvePoNumber(excelRow.PoNumber, excelRow.JobNumber);
            fieldChanges.Add(new OeFieldChange { FieldLabel = "P.O. :", OldValue = dbRow.PoNumber, NewValue = newPoNumber });
        }

        if (!TextEqualsCI(excelRow.Customer, dbRow.CustomerName))
            fieldChanges.Add(new OeFieldChange { FieldLabel = "Customer:", OldValue = dbRow.CustomerName, NewValue = excelRow.Customer });

        if (!TextEqualsCI(excelRow.Contact, dbRow.ContactName))
            fieldChanges.Add(new OeFieldChange { FieldLabel = "Contact:", OldValue = dbRow.ContactName, NewValue = excelRow.Contact });

        return fieldChanges;
    }

    private static void TryAddAdd(List<OeSyncChange> changes, OeExcelRow excelRow,
        IReadOnlyDictionary<(string Job, string Line), OeAnomalyHandled> handledAnomalies)
    {
        if (string.IsNullOrWhiteSpace(excelRow.Quantity))
        {
            AddAnomaly(changes, excelRow, null, "Qty is blank, cannot add, needs manual handling", handledAnomalies);
            return;
        }
        if (string.IsNullOrWhiteSpace(excelRow.LineNumber))
        {
            AddAnomaly(changes, excelRow, null, "M column is blank, cannot add, needs manual handling", handledAnomalies);
            return;
        }
        // A natural-key collision with an existing (active or archived) order_item is caught
        // upstream in ComputeDiff's main matching loop before a row ever reaches pendingAddRows —
        // see the allOrderItems lookup there — so by this point the key is guaranteed free.

        changes.Add(new OeSyncChange
        {
            Kind = OeSyncChangeKind.Add,
            HeaderText = $"Job #{excelRow.JobNumber} / M{excelRow.LineNumber} / {excelRow.PartNumber}",
            ExcelRow = excelRow,
        });
    }

    /// <param name="reactivateItem">
    /// True when dbRow was matched by natural key while archived (its own is_active = 0) — see
    /// ComputeDiff's allOrderItems lookup. A purchase_order's active status is fully derived from
    /// its order_items, so reactivating a natural-key match is automatic, not a judgment call:
    /// folded into this Modify (not a separate Anomaly) so Update/Update-All apply it normally.
    /// </param>
    /// <param name="reactivatePo">Same, for the owning purchase_order (its own is_active = 0).</param>
    private static void TryAddModify(List<OeSyncChange> changes, OeExcelRow excelRow, OeDbRow dbRow,
        IReadOnlyDictionary<(string Job, string Line), OeAnomalyHandled> handledAnomalies,
        bool reactivateItem = false, bool reactivatePo = false)
    {
        var fieldChanges = BuildNonQuantityFieldChanges(excelRow, dbRow);

        if (string.IsNullOrWhiteSpace(excelRow.Quantity))
        {
            // Qty can't be diffed/applied blank, but every other field difference already computed
            // above still needs to reach the reviewer — carried on the Anomaly so a corrected Qty
            // (typed into the review dialog) can still apply the rest of this row's changes too,
            // including reactivation (OeAnomalyEditDialog reads NeedsItemReactivation/NeedsPoReactivation).
            AddAnomaly(changes, excelRow, dbRow, "Qty is blank, needs manual handling", handledAnomalies,
                fieldChanges: fieldChanges, needsItemReactivation: reactivateItem, needsPoReactivation: reactivatePo);
            return;
        }

        var (excelQtyStored, _) = OeNormalization.ResolveQuantity(excelRow.Quantity);
        var (dbQtyStored, _) = OeNormalization.ResolveQuantity(dbRow.Quantity);
        if (excelQtyStored != dbQtyStored)
            fieldChanges.Add(new OeFieldChange { FieldLabel = "Qty.:", OldValue = dbQtyStored, NewValue = excelQtyStored });

        // Even with zero field diffs, a reactivation still needs to apply — the row just won't have
        // anything to show in the FieldChanges panel besides the reactivation itself.
        if (fieldChanges.Count == 0 && !reactivateItem && !reactivatePo) return; // identical — nothing to review

        var fingerprint = ComputeModifyFingerprint(fieldChanges);

        DateTime? handledAt = null;
        string? handledAction = null;
        bool wasPreHandled = false;
        if (handledAnomalies.TryGetValue(DbMatchKeyOf(dbRow), out var handled) && handled.Fingerprint == fingerprint)
        {
            handledAt = handled.HandledAt;
            handledAction = handled.Action;
            wasPreHandled = true;
        }

        var modifyKey = DbMatchKeyOf(dbRow);
        changes.Add(new OeSyncChange
        {
            Kind = OeSyncChangeKind.Modify,
            HeaderText = $"Job #{excelRow.JobNumber} / M{excelRow.LineNumber} / {excelRow.PartNumber}" +
                (reactivateItem || reactivatePo ? " (reactivating)" : ""),
            ExcelRow = excelRow,
            DbRow = dbRow,
            OrderItemId = dbRow.OrderItemId,
            PoId = dbRow.PoId,
            FieldChanges = fieldChanges,
            Fingerprint = fingerprint,
            HandledJobNumber = modifyKey.Job,
            HandledLineNumber = modifyKey.Line,
            NeedsItemReactivation = reactivateItem,
            NeedsPoReactivation = reactivatePo,
            WasPreHandled = wasPreHandled,
            Status = wasPreHandled ? OeSyncRowStatus.Ignored : OeSyncRowStatus.None,
            HandledAt = handledAt,
            HandledAction = handledAction,
        });
    }

    private static void AddAnomaly(List<OeSyncChange> changes, OeExcelRow? excelRow, OeDbRow? dbRow,
        string reason, IReadOnlyDictionary<(string Job, string Line), OeAnomalyHandled> handledAnomalies,
        List<OeExcelRow>? siblings = null, List<OeFieldChange>? fieldChanges = null,
        bool isLineNumberRenameCandidate = false,
        bool needsItemReactivation = false, bool needsPoReactivation = false)
    {
        var header = excelRow != null
            ? $"Job #{excelRow.JobNumber} / M{excelRow.LineNumber} / {excelRow.PartNumber}"
            : $"Job #{dbRow!.JobNumber} / M{dbRow.LineNumber} / {dbRow.DrawingNumber}";

        var fingerprint = ComputeAnomalyFingerprint(reason, excelRow, dbRow);

        DateTime? handledAt = null;
        string? handledAction = null;
        bool wasPreHandled = false;
        var key = excelRow != null ? MatchKeyOf(excelRow) : DbMatchKeyOf(dbRow!);
        if (key.Job.Length > 0 && handledAnomalies.TryGetValue(key, out var handled) && handled.Fingerprint == fingerprint)
        {
            handledAt = handled.HandledAt;
            handledAction = handled.Action;
            wasPreHandled = true;
        }

        changes.Add(new OeSyncChange
        {
            Kind = OeSyncChangeKind.Anomaly,
            HeaderText = header,
            ExcelRow = excelRow,
            DbRow = dbRow,
            OrderItemId = dbRow?.OrderItemId,
            PoId = dbRow?.PoId,
            SiblingExcelRows = siblings ?? new List<OeExcelRow>(),
            AnomalyReason = reason,
            FieldChanges = fieldChanges ?? new List<OeFieldChange>(),
            IsLineNumberRenameCandidate = isLineNumberRenameCandidate,
            NeedsItemReactivation = needsItemReactivation,
            NeedsPoReactivation = needsPoReactivation,
            Fingerprint = fingerprint,
            HandledJobNumber = key.Job,
            HandledLineNumber = key.Line,
            WasPreHandled = wasPreHandled,
            Status = wasPreHandled ? OeSyncRowStatus.Ignored : OeSyncRowStatus.None,
            HandledAt = handledAt,
            HandledAction = handledAction,
        });
    }

    private static bool TextEquals(string? a, string? b)
        => OeNormalization.NormalizeText(a) == OeNormalization.NormalizeText(b);

    private static bool TextEqualsCI(string? a, string? b)
        => string.Equals(OeNormalization.NormalizeText(a), OeNormalization.NormalizeText(b), StringComparison.OrdinalIgnoreCase);

    private static bool PricesEqual(decimal? a, decimal? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return Math.Abs(a.Value - b.Value) < 0.005m;
    }

    private static string FormatPrice(decimal? p) => p.HasValue ? p.Value.ToString("0.00", CultureInfo.InvariantCulture) : "";
}
