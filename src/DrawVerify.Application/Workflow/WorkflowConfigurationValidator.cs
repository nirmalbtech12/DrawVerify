using DrawVerify.Core.Configuration;
namespace DrawVerify.Application.Workflow;
public sealed class WorkflowConfigurationValidator
{
    public IReadOnlyList<string> Validate(WorkflowConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var errors = new List<string>(configuration.ApplicationValidation());
        var transitions = configuration.StatusTransitions.GroupBy(x => x.From).ToDictionary(x => x.Key, x => x.Select(y => y.To).ToArray());
        foreach (var state in transitions.Keys)
        {
            var visited = new HashSet<DrawVerify.Core.Enums.WorkflowStatus>();
            var pending = new Stack<DrawVerify.Core.Enums.WorkflowStatus>(new[] { state });
            while (pending.Count > 0)
            {
                var current = pending.Pop();
                if (!visited.Add(current)) { errors.Add($"StatusTransitions contains a circular route involving '{current}'."); break; }
                if (transitions.TryGetValue(current, out var next)) foreach (var item in next) pending.Push(item);
            }
        }
        if (!string.Equals(configuration.RevisionPolicy.NumberFormat, "Sequential", StringComparison.OrdinalIgnoreCase) && !string.Equals(configuration.RevisionPolicy.NumberFormat, "Major.Minor", StringComparison.OrdinalIgnoreCase)) errors.Add("RevisionPolicy.NumberFormat must be Sequential or Major.Minor.");
        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }
    public void EnsureValid(WorkflowConfiguration configuration)
    { var errors = Validate(configuration); if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors)); }
}
