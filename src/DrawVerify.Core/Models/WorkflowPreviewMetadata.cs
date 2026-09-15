namespace DrawVerify.Core.Models;
public sealed record WorkflowPreviewMetadata(string? DocumentType = null, string? Project = null, string? Department = null, int Revision = 1, int? RiskLevel = null, decimal? Value = null);
