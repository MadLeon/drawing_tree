namespace DrawingTree.Models;

/// <summary>
/// One row of the oe_anomaly_handled memory table (Issue #44 follow-up): records that a human
/// already reviewed a given (job_number, line_number) Modify or Anomaly change, so a future OE
/// sync run that proposes the exact same field value(s) again — matched via
/// <see cref="Fingerprint"/>, not free text — can suppress it instead of re-nagging from scratch.
/// </summary>
public class OeAnomalyHandled
{
    /// <summary>Human-readable summary, kept for debugging/display only — not used for matching.</summary>
    public string AnomalyReason { get; init; } = "";

    /// <summary>Hash of the specific proposed field value(s); see OeSyncService.ComputeFingerprint.
    /// This, not AnomalyReason, is what decides whether a freshly-detected change is "the same one"
    /// the reviewer already handled.</summary>
    public string Fingerprint { get; init; } = "";

    /// <summary>"ignored" or "corrected".</summary>
    public string Action { get; init; } = "";

    public DateTime HandledAt { get; init; }
}
