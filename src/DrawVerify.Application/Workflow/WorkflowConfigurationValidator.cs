using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
namespace DrawVerify.Application.Workflow;
public sealed class WorkflowConfigurationValidator
{
    public IReadOnlyList<string> Validate(WorkflowConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var errors = new List<string>(configuration.ApplicationValidation());
        var roleNames = configuration.Roles.Count == 0 ? SystemRoles.Catalog.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase) : configuration.Roles.Select(x => x.DisplayName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stageNames = configuration.Stages.Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!Enum.IsDefined(configuration.RolloutPolicy)) errors.Add($"RolloutPolicy '{configuration.RolloutPolicy}' is unsupported.");
        if (string.IsNullOrWhiteSpace(configuration.RevisionPolicy.NumberFormat) || !new[] { "Sequential", "Major.Minor" }.Contains(configuration.RevisionPolicy.NumberFormat, StringComparer.OrdinalIgnoreCase)) errors.Add("RevisionPolicy.NumberFormat must be Sequential or Major.Minor.");
        foreach (var role in configuration.Roles)
        {
            if (!SystemRoles.Catalog.Any(x => string.Equals(x.Id, role.SystemRole, StringComparison.OrdinalIgnoreCase))) errors.Add($"Roles['{role.DisplayName}'].SystemRole '{role.SystemRole}' is undefined.");
            if (role.Permissions.Any(permission => !Enum.IsDefined(permission))) errors.Add($"Roles['{role.DisplayName}'].Permissions contains an unsupported permission.");
        }
        foreach (var stage in configuration.Stages)
        {
            if (string.IsNullOrWhiteSpace(stage.Name)) errors.Add("Stages[].Name is required.");
            if (!Enum.IsDefined(stage.Mode)) errors.Add($"Stages['{stage.Name}'].Mode '{stage.Mode}' is unsupported.");
            if (stage.Roles.Count == 0) errors.Add($"Stages['{stage.Name}'].Roles must define at least one approver role.");
            if (stage.RequiredApprovals < 1 || stage.RequiredApprovals > stage.Roles.Count) errors.Add($"Stages['{stage.Name}'].RequiredApprovals must be between 1 and the assigned role count.");
            foreach (var role in stage.Roles) if (!roleNames.Contains(role)) errors.Add($"Stages['{stage.Name}'].Roles references undefined role '{role}'.");
            if (!string.IsNullOrWhiteSpace(stage.RejectTo) && !roleNames.Contains(stage.RejectTo) && !stageNames.Contains(stage.RejectTo)) errors.Add($"Stages['{stage.Name}'].RejectTo references undefined route '{stage.RejectTo}'.");
        }
        if (stageNames.Count != configuration.Stages.Count) errors.Add("Stages.Name values must be unique and non-empty.");
        foreach (var trigger in configuration.Triggers)
        {
            if (string.IsNullOrWhiteSpace(trigger.Name)) errors.Add("Triggers[].Name is required.");
            ValidateStrings(errors, $"Triggers['{trigger.Name}'].DocumentTypes", trigger.DocumentTypes); ValidateStrings(errors, $"Triggers['{trigger.Name}'].Projects", trigger.Projects); ValidateStrings(errors, $"Triggers['{trigger.Name}'].Departments", trigger.Departments);
            if (trigger.MinimumRevision is < 0) errors.Add($"Triggers['{trigger.Name}'].MinimumRevision cannot be negative."); if (trigger.RiskLevel is < 0) errors.Add($"Triggers['{trigger.Name}'].RiskLevel cannot be negative."); if (trigger.MinimumValue is < 0) errors.Add($"Triggers['{trigger.Name}'].MinimumValue cannot be negative.");
        }
        foreach (var transition in configuration.StatusTransitions)
        {
            if (transition.From == transition.To) errors.Add($"StatusTransitions['{transition.From}'].To cannot equal From.");
            if (transition.AuthorizedRoles.Count == 0) errors.Add($"StatusTransitions['{transition.From}->{transition.To}'].AuthorizedRoles is required.");
            foreach (var role in transition.AuthorizedRoles) if (!roleNames.Contains(role)) errors.Add($"StatusTransitions['{transition.From}->{transition.To}'] references undefined role '{role}'.");
        }
        ValidateTransitionGraph(configuration, errors);
        foreach (var label in configuration.StatusLabels) if (string.IsNullOrWhiteSpace(label.Value)) errors.Add($"StatusLabels['{label.Key}'] cannot be blank.");
        foreach (var notification in configuration.Notifications)
        {
            if (notification.RecipientRoles.Count == 0) errors.Add($"Notifications['{notification.Event}'].RecipientRoles is required."); if (notification.Channels.Count == 0) errors.Add($"Notifications['{notification.Event}'].Channels is required.");
            foreach (var channel in notification.Channels) if (!Enum.IsDefined(channel) || channel is not WorkflowNotificationChannel.InApp and not WorkflowNotificationChannel.Email) errors.Add($"Notifications['{notification.Event}'] contains unsupported notification channel '{channel}'.");
            foreach (var role in notification.RecipientRoles) if (!roleNames.Contains(role)) errors.Add($"Notifications['{notification.Event}'] references undefined role '{role}'.");
            if (notification.ReminderEnabled && notification.ReminderInterval <= TimeSpan.Zero) errors.Add($"Notifications['{notification.Event}'].ReminderInterval must be positive when reminders are enabled."); if (string.IsNullOrWhiteSpace(notification.ContentTemplate)) errors.Add($"Notifications['{notification.Event}'].ContentTemplate is required.");
        }
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }
    private static void ValidateStrings(ICollection<string> errors, string field, IEnumerable<string> values) { if (values.Any(string.IsNullOrWhiteSpace)) errors.Add($"{field} contains a blank condition."); }
    private static void ValidateTransitionGraph(WorkflowConfiguration configuration, ICollection<string> errors)
    {
        var graph = configuration.StatusTransitions.GroupBy(x => x.From).ToDictionary(x => x.Key, x => x.Select(y => y.To).ToArray()); var colors = new Dictionary<WorkflowStatus, int>();
        bool Visit(WorkflowStatus node) { if (colors.TryGetValue(node, out var color)) return color == 1; colors[node] = 1; if (graph.TryGetValue(node, out var next) && next.Any(Visit)) return true; colors[node] = 2; return false; }
        foreach (var node in graph.Keys) if (Visit(node)) { errors.Add($"StatusTransitions contains a circular route involving '{node}'."); break; }

        if (configuration.StatusTransitions.Count > 0)
        {
            var roots = new HashSet<WorkflowStatus> { WorkflowStatus.Draft };
            if (configuration.StatusLabels.TryGetValue(WorkflowStatus.Draft, out var draftLabel) && Enum.TryParse<WorkflowStatus>(draftLabel, true, out var labelledDraft)) roots.Add(labelledDraft);
            var reachable = new HashSet<WorkflowStatus>(roots);
            var pending = new Queue<WorkflowStatus>(roots);
            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                if (!graph.TryGetValue(current, out var next)) continue;
                foreach (var target in next.Where(reachable.Add)) pending.Enqueue(target);
            }
            foreach (var transition in configuration.StatusTransitions.Where(x => !reachable.Contains(x.From)))
                errors.Add($"StatusTransitions['{transition.From}->{transition.To}'] is unreachable from '{WorkflowStatus.Draft}'.");
        }

        var rejectionGraph = configuration.Stages.Where(x => !string.IsNullOrWhiteSpace(x.RejectTo))
            .ToDictionary(x => x.Name, x => x.RejectTo!, StringComparer.OrdinalIgnoreCase);
        var rejectionColors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        bool VisitRejection(string stage)
        {
            if (rejectionColors.TryGetValue(stage, out var color)) return color == 1;
            rejectionColors[stage] = 1;
            if (rejectionGraph.TryGetValue(stage, out var next) && VisitRejection(next)) return true;
            rejectionColors[stage] = 2;
            return false;
        }
        foreach (var stage in rejectionGraph.Keys)
            if (VisitRejection(stage)) errors.Add($"Stages['{stage}'].RejectTo contains a circular rejection route.");
    }
    public void EnsureValid(WorkflowConfiguration configuration)
    { var errors = Validate(configuration); if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors)); }
}
