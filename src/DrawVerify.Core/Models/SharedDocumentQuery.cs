namespace DrawVerify.Core.Models;

public sealed record SharedDocumentQuery(string? Search = null)
{
    public string? NormalizedSearch => string.IsNullOrWhiteSpace(Search) ? null : Search.Trim();
}
