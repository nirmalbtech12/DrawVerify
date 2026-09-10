using System.Globalization;
using System.Text;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public enum MissingDimensionSortField { Reference, Layer, Block, View, X, Y }

public sealed record MissingDimensionQuery(string? Layer = null, string? Block = null, string? View = null, MissingDimensionSortField SortBy = MissingDimensionSortField.Reference, bool Descending = false);

public sealed record MissingDimensionSummary(int Total, IReadOnlyDictionary<string, int> ByLayer, IReadOnlyDictionary<string, int> ByView);

public sealed class MissingDimensionReviewService
{
    private readonly IVerificationSessionRepository _sessions;
    private readonly ISessionAuthenticationService _authentication;
    private readonly RoleAuthorization _authorization;
    private readonly ICentralizedLogger? _logger;

    public MissingDimensionReviewService(IVerificationSessionRepository sessions, ISessionAuthenticationService authentication, RoleAuthorization? authorization = null, ICentralizedLogger? logger = null)
    { _sessions = sessions; _authentication = authentication; _authorization = authorization ?? new RoleAuthorization(); _logger = logger; }

    public async Task<IReadOnlyList<DimensionMatch>> QueryAsync(Guid sessionId, MissingDimensionQuery? query = null, CancellationToken cancellationToken = default)
    {
        Ensure(RolePermission.ViewMissingDimensions);
        if (sessionId == Guid.Empty) throw new ArgumentException("A verification session is required.", nameof(sessionId));
        query ??= new MissingDimensionQuery();
        var matches = await _sessions.GetMatchesAsync(sessionId, DimensionMatchStatus.Unmatched, cancellationToken).ConfigureAwait(false);
        var filtered = matches.Where(m => Matches(m.DxfLayer, query.Layer) && Matches(m.BlockName, query.Block) && Matches(m.ViewName, query.View));
        Func<DimensionMatch, object?> key = query.SortBy switch
        {
            MissingDimensionSortField.Layer => m => m.DxfLayer ?? string.Empty,
            MissingDimensionSortField.Block => m => m.BlockName ?? string.Empty,
            MissingDimensionSortField.View => m => m.ViewName ?? string.Empty,
            MissingDimensionSortField.X => m => m.LocationX is null,
            MissingDimensionSortField.Y => m => m.LocationY is null,
            _ => m => m.DxfDimensionReference
        };
        var ordered = query.SortBy switch
        {
            MissingDimensionSortField.X => query.Descending ? filtered.OrderByDescending(m => m.LocationX is null).ThenByDescending(m => m.LocationX).ThenBy(m => m.DxfDimensionReference) : filtered.OrderBy(m => m.LocationX is null).ThenBy(m => m.LocationX).ThenBy(m => m.DxfDimensionReference),
            MissingDimensionSortField.Y => query.Descending ? filtered.OrderByDescending(m => m.LocationY is null).ThenByDescending(m => m.LocationY).ThenBy(m => m.DxfDimensionReference) : filtered.OrderBy(m => m.LocationY is null).ThenBy(m => m.LocationY).ThenBy(m => m.DxfDimensionReference),
            _ => query.Descending ? filtered.OrderByDescending(key).ThenBy(m => m.DxfDimensionReference) : filtered.OrderBy(key).ThenBy(m => m.DxfDimensionReference)
        };
        return ordered.ToArray();
    }

    public async Task<MissingDimensionSummary> GetSummaryAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var items = await QueryAsync(sessionId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new MissingDimensionSummary(items.Count, Group(items, m => m.DxfLayer), Group(items, m => m.ViewName));
    }
    public async Task<IReadOnlyList<DimensionMatch>> QueryExtraAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        Ensure(RolePermission.ViewExtraDimensions);
        if (sessionId == Guid.Empty) throw new ArgumentException("A verification session is required.", nameof(sessionId));
        var matches = await _sessions.GetMatchesAsync(sessionId, cancellationToken: cancellationToken).ConfigureAwait(false);
        return matches.Where(item => DimensionMatchingService.IsExtraDimensionStatus(item.Status)).ToArray();
    }

    private void Ensure(RolePermission permission)
    { if (!_authentication.IsAuthenticated || !_authorization.IsAllowed(_authentication.CurrentUser, permission)) throw new UnauthorizedAccessException("You are not authorized to review missing dimensions."); }
    private static bool Matches(string? actual, string? requested) => string.IsNullOrWhiteSpace(requested) || string.Equals(actual, requested.Trim(), StringComparison.OrdinalIgnoreCase);
    private static IReadOnlyDictionary<string, int> Group(IEnumerable<DimensionMatch> items, Func<DimensionMatch, string?> selector) => items.GroupBy(selector).Where(g => !string.IsNullOrWhiteSpace(g.Key)).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key!, g => g.Count(), StringComparer.OrdinalIgnoreCase);
}
