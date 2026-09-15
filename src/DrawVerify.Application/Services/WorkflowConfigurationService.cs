using System.Text.Json;
using DrawVerify.Application.Workflow;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
namespace DrawVerify.Application.Services;
public sealed class WorkflowConfigurationService : IWorkflowConfigurationService
{
    private readonly IWorkflowConfigurationRepository _repository;
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    private readonly ICentralizedLogger? _logger;
    private readonly WorkflowConfigurationValidator _validator = new();
    private readonly Func<DateTime> _clock;
    public WorkflowConfigurationService(IWorkflowConfigurationRepository repository, ISessionAuthenticationService session, RoleAuthorization? authorization = null, ICentralizedLogger? logger = null, Func<DateTime>? clock = null)
    { _repository = repository ?? throw new ArgumentNullException(nameof(repository)); _session = session ?? throw new ArgumentNullException(nameof(session)); _authorization = authorization ?? new(); _logger = logger; _clock = clock ?? (() => DateTime.UtcNow); }
    public async Task<WorkflowConfiguration> GetAsync(CancellationToken cancellationToken = default) { Authorize(); return await _repository.GetCurrentAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("No workflow configuration is available."); }
    public async Task<WorkflowConfiguration> SaveAsync(WorkflowConfiguration configuration, int? expectedVersion = null, CancellationToken cancellationToken = default)
    {
        Authorize(); _validator.EnsureValid(configuration);
        var previous = await _repository.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var next = configuration with { Version = Math.Max(configuration.Version, (previous?.Version ?? 0) + 1) };
        var actor = _session.CurrentUser!.UserId;
        var audit = new WorkflowConfigurationAudit(actor, _clock().ToUniversalTime(), previous?.Version, next.Version, JsonSerializer.Serialize(previous), JsonSerializer.Serialize(next));
        try { return await _repository.SaveAsync(next, audit, expectedVersion ?? previous?.Version, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) { _logger?.Log(exception, "Workflow configuration save", $"Unable to save workflow configuration version={next.Version}; actor={actor}."); throw; }
    }
    public Task<WorkflowPreviewResult> PreviewAsync(WorkflowConfiguration configuration, WorkflowPreviewMetadata metadata, CancellationToken cancellationToken = default)
    {
        Authorize(); _validator.EnsureValid(configuration); cancellationToken.ThrowIfCancellationRequested();
        var matches = configuration.Triggers.Count == 0 || configuration.Triggers.Any(t => (t.DocumentTypes.Count == 0 || t.DocumentTypes.Contains(metadata.DocumentType ?? string.Empty, StringComparer.OrdinalIgnoreCase)) && (t.Projects.Count == 0 || t.Projects.Contains(metadata.Project ?? string.Empty, StringComparer.OrdinalIgnoreCase)) && (t.Departments.Count == 0 || t.Departments.Contains(metadata.Department ?? string.Empty, StringComparer.OrdinalIgnoreCase)) && (!t.MinimumRevision.HasValue || metadata.Revision >= t.MinimumRevision) && (!t.RiskLevel.HasValue || (metadata.RiskLevel ?? 0) >= t.RiskLevel) && (!t.MinimumValue.HasValue || (metadata.Value ?? 0) >= t.MinimumValue));
        var stages = matches ? configuration.Stages.Select(x => x.Name).ToArray() : Array.Empty<string>();
        var recipients = configuration.Notifications.SelectMany(x => x.RecipientRoles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return Task.FromResult(new WorkflowPreviewResult(stages, recipients, Array.Empty<string>(), matches));
    }
    private void Authorize() { if (!_session.IsAuthenticated || !_authorization.IsAllowed(_session.CurrentUser, RolePermission.ManageGlobalConfiguration)) throw new UnauthorizedAccessException("Only an authenticated Admin can manage workflow configuration."); }
}
