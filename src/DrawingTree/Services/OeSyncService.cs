/// <summary>
/// OeSyncService.cs
/// Pure diff engine for the OE sync feature (Issue #44): compares the current OE Excel content
/// ("set A") against the DB snapshot of active order_items ("set B") and classifies every
/// difference as Add / Modify / Deactivate / Anomaly. No file or database access here — this
/// class only transforms data already read by OeExcelParser / OeSyncRepository.
/// </summary>

using System.Globalization;
using DrawingTree.Models;

namespace DrawingTree.Services;

public static class OeSyncService
{
    /// <summary>Computes every pending OE sync change between the Excel rows and the DB snapshot.</summary>
    /// <param name="excelRows">Rows parsed from the OE Excel file ("set A").</param>
    /// <param name="dbRows">Active order_items under active POs ("set B").</param>
    /// <param name="allExistingKeys">
    /// (job_number, line_number) of every order_item in the database regardless of active status.
    /// A would-be Add whose key collides here is a historical/archived row reappearing in the
    /// append-only Excel log, not a genuinely new item — it is routed to Anomaly instead, since
    /// inserting it would violate UNIQUE(job_id, line_number) and the reactivate-vs-ignore call
    /// belongs to a human. Pass null/empty to skip this check (e.g. in isolated unit tests).
    /// </param>
    public static List<OeSyncChange> ComputeDiff(List<OeExcelRow> excelRows, List<OeDbRow> dbRows,
        ISet<(string Job, string Line)>? allExistingKeys = null)
    {
        allExistingKeys ??= new HashSet<(string, string)>();
        var changes = new List<OeSyncChange>();

        var warningRows = excelRows.Where(r => r.ParseWarnings.Count > 0).ToList();
        var candidateRows = excelRows.Where(r => r.ParseWarnings.Count == 0).ToList();

        foreach (var r in warningRows)
            AddAnomaly(changes, r, null, string.Join("; ", r.ParseWarnings));

        var excelGroups = candidateRows.GroupBy(MatchKeyOf).ToList();
        var excelByKey = new Dictionary<(string Job, string Line), OeExcelRow>();
        foreach (var g in excelGroups)
        {
            if (g.Key.Job.Length == 0)
            {
                foreach (var r in g) AddAnomaly(changes, r, null, "Job # is blank, cannot locate the corresponding order");
                continue;
            }
            if (g.Count() > 1)
            {
                foreach (var r in g)
                    AddAnomaly(changes, r, null, $"The same Job # + M column appears {g.Count()} times, cannot determine a unique match");
                continue;
            }
            excelByKey[g.Key] = g.Single();
        }

        var dbByKey = new Dictionary<(string Job, string Line), OeDbRow>();
        foreach (var d in dbRows)
            dbByKey[DbMatchKeyOf(d)] = d; // job_number + line_number is unique per schema

        var matchedDbKeys = new HashSet<(string, string)>();

        foreach (var (key, excelRow) in excelByKey)
        {
            if (dbByKey.TryGetValue(key, out var dbRow))
            {
                matchedDbKeys.Add(key);
                TryAddModify(changes, excelRow, dbRow);
            }
            else
            {
                TryAddAdd(changes, excelRow, allExistingKeys, key);
            }
        }

        var matchedPoIds = dbRows.Where(d => matchedDbKeys.Contains(DbMatchKeyOf(d))).Select(d => d.PoId).ToHashSet();
        var unmatchedByPo = dbRows.Where(d => !matchedDbKeys.Contains(DbMatchKeyOf(d))).GroupBy(d => d.PoId);

        foreach (var group in unmatchedByPo)
        {
            var items = group.ToList();
            if (matchedPoIds.Contains(group.Key))
            {
                var siblings = excelRows
                    .Where(r => OeNormalization.ArePoNumbersEquivalent(r.PoNumber, items[0].PoNumber))
                    .ToList();
                foreach (var d in items)
                    AddAnomaly(changes, null, d,
                        "Some rows under this PO are missing from the OE file, but other rows under the same PO still exist — needs manual confirmation of whether this has shipped", siblings);
            }
            else
            {
                changes.Add(new OeSyncChange
                {
                    Kind = OeSyncChangeKind.Deactivate,
                    HeaderText = $"PO {items[0].PoNumber} (Job #{FormatJobNumberRange(items)}) — " +
                        $"{items.Count} row(s) missing from the OE file (shipped)",
                    DbRow = items[0],
                    PoId = group.Key,
                    DeactivateItems = items,
                });
            }
        }

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

    private static void TryAddAdd(List<OeSyncChange> changes, OeExcelRow excelRow,
        ISet<(string Job, string Line)> allExistingKeys, (string Job, string Line) key)
    {
        if (string.IsNullOrWhiteSpace(excelRow.Quantity))
        {
            AddAnomaly(changes, excelRow, null, "Qty is blank, cannot add, needs manual handling");
            return;
        }
        if (string.IsNullOrWhiteSpace(excelRow.LineNumber))
        {
            AddAnomaly(changes, excelRow, null, "M column is blank, cannot add, needs manual handling");
            return;
        }
        if (allExistingKeys.Contains(key))
        {
            AddAnomaly(changes, excelRow, null,
                "This Job # + M already exists in the database, but the linked PO/order_item is currently inactive " +
                "(a historical/archived row) — cannot add directly, needs manual confirmation of whether it should be reactivated");
            return;
        }

        changes.Add(new OeSyncChange
        {
            Kind = OeSyncChangeKind.Add,
            HeaderText = $"Job #{excelRow.JobNumber} / M{excelRow.LineNumber} / {excelRow.PartNumber}",
            ExcelRow = excelRow,
        });
    }

    private static void TryAddModify(List<OeSyncChange> changes, OeExcelRow excelRow, OeDbRow dbRow)
    {
        if (string.IsNullOrWhiteSpace(excelRow.Quantity))
        {
            AddAnomaly(changes, excelRow, dbRow, "Qty is blank, needs manual handling");
            return;
        }

        var (excelQtyStored, _) = OeNormalization.ResolveQuantity(excelRow.Quantity);
        var (dbQtyStored, _) = OeNormalization.ResolveQuantity(dbRow.Quantity);

        var fieldChanges = new List<OeFieldChange>();

        if (excelQtyStored != dbQtyStored)
            fieldChanges.Add(new OeFieldChange { FieldLabel = "Qty.:", OldValue = dbQtyStored, NewValue = excelQtyStored });

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

        if (!OeNormalization.ArePoNumbersEquivalent(excelRow.PoNumber, dbRow.PoNumber))
            fieldChanges.Add(new OeFieldChange { FieldLabel = "P.O. :", OldValue = dbRow.PoNumber, NewValue = excelRow.PoNumber });

        if (!TextEqualsCI(excelRow.Customer, dbRow.CustomerName))
            fieldChanges.Add(new OeFieldChange { FieldLabel = "Customer:", OldValue = dbRow.CustomerName, NewValue = excelRow.Customer });

        if (!TextEqualsCI(excelRow.Contact, dbRow.ContactName))
            fieldChanges.Add(new OeFieldChange { FieldLabel = "Contact:", OldValue = dbRow.ContactName, NewValue = excelRow.Contact });

        if (fieldChanges.Count == 0) return; // identical — nothing to review

        changes.Add(new OeSyncChange
        {
            Kind = OeSyncChangeKind.Modify,
            HeaderText = $"Job #{excelRow.JobNumber} / M{excelRow.LineNumber} / {excelRow.PartNumber}",
            ExcelRow = excelRow,
            DbRow = dbRow,
            OrderItemId = dbRow.OrderItemId,
            PoId = dbRow.PoId,
            FieldChanges = fieldChanges,
        });
    }

    private static void AddAnomaly(List<OeSyncChange> changes, OeExcelRow? excelRow, OeDbRow? dbRow,
        string reason, List<OeExcelRow>? siblings = null)
    {
        var header = excelRow != null
            ? $"Job #{excelRow.JobNumber} / M{excelRow.LineNumber} / {excelRow.PartNumber}"
            : $"Job #{dbRow!.JobNumber} / M{dbRow.LineNumber} / {dbRow.DrawingNumber}";

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
