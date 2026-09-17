using System.Collections.Generic;

namespace DrawVerify.Core.Models;

public sealed record VerificationReportRequest
{
    public bool MismatchedAndMissingOnly { get; init; }
    public bool GdtOnly { get; init; }
    public IReadOnlyList<string> FeatureFilters { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ZoneFilters { get; init; } = Array.Empty<string>();
    public bool IncludeVisuals { get; init; }
    public IReadOnlyList<VerificationReportVisual> Visuals { get; init; } = Array.Empty<VerificationReportVisual>();
    public bool IncludeVerificationAuditOnly { get; init; }
    public bool IncludeApprovalAuditOnly { get; init; }
    public DateTimeOffset? AuditFromUtc { get; init; }
    public DateTimeOffset? AuditToUtc { get; init; }
    public string? AuditUserId { get; init; }

    public VerificationReportRequest Normalize()
    {
        if (MismatchedAndMissingOnly && GdtOnly) throw new ArgumentException("Dimension-only and GD&T-only report scopes cannot be combined.");
        if (IncludeVerificationAuditOnly && IncludeApprovalAuditOnly) throw new ArgumentException("Verification-only and approval-only audit scopes cannot be combined.");
        if (AuditFromUtc.HasValue && AuditToUtc.HasValue && AuditFromUtc > AuditToUtc) throw new ArgumentException("Audit scope start must not be after its end.");
        var features = FeatureFilters.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        var zones = ZoneFilters.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order().ToArray();
        return this with { FeatureFilters = features, ZoneFilters = zones, AuditUserId = string.IsNullOrWhiteSpace(AuditUserId) ? null : AuditUserId.Trim(), Visuals = IncludeVisuals ? Visuals : Array.Empty<VerificationReportVisual>() };
    }
}

public sealed record VerificationReportVisual(string Reference, string SnapshotPath, string? Caption = null);
