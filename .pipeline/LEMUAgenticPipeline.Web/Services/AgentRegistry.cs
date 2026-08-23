using LEMUAgenticPipeline.Web.Models;
using Microsoft.Extensions.Options;

namespace LEMUAgenticPipeline.Web.Services;

public sealed class AgentRegistryOptions
{
    public List<AgentDefinition> Agents { get; set; } = DefaultAgents.All;
    public List<string> DefaultSelected { get; set; } = [];
}

public sealed class AgentRegistry
{
    private readonly List<AgentDefinition> _all;
    private readonly HashSet<string> _default;
    public AgentRegistry(IOptions<AgentRegistryOptions> opts)
    {
        _all = opts.Value.Agents.Where(a => a.Enabled).OrderBy(a => a.ExecutionOrder).ToList();
        _default = new HashSet<string>(opts.Value.DefaultSelected, StringComparer.OrdinalIgnoreCase);
    }
    public IReadOnlyList<AgentDefinition> All => _all;
    public IReadOnlyList<AgentDefinition> DefaultPipeline => _all.Where(a => _default.Contains(a.AgentId)).ToList();

    public (bool Valid, List<string> Errors, List<string> AutoSelected) ValidateSelection(IEnumerable<string> selectedIds)
    {
        var selected = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();
        var auto = new List<string>();

        // Check unknown ids
        foreach (var id in selected)
            if (!_all.Any(a => a.AgentId.Equals(id, StringComparison.OrdinalIgnoreCase)))
                errors.Add($"Unknown agent: {id}");

        // Check dependencies
        foreach (var id in selected.ToList())
        {
            var def = _all.FirstOrDefault(a => a.AgentId.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (def == null) continue;
            foreach (var dep in def.Dependencies)
            {
                if (!selected.Contains(dep))
                {
                    // Auto-select per spec §9
                    var depDef = _all.FirstOrDefault(a => a.AgentId.Equals(dep, StringComparison.OrdinalIgnoreCase));
                    if (depDef != null)
                    {
                        selected.Add(dep);
                        auto.Add(dep);
                    }
                    else errors.Add($"{def.Name} requires {dep} (not found)");
                }
            }
        }

        // Also validate transitive dependencies (e.g., Implementation -> Test Design -> Technical Design)
        bool changed;
        do
        {
            changed = false;
            foreach (var id in selected.ToList())
            {
                var def = _all.First(a => a.AgentId.Equals(id, StringComparison.OrdinalIgnoreCase));
                foreach (var dep in def.Dependencies)
                    if (selected.Add(dep)) { auto.Add(dep); changed = true; }
            }
        } while (changed);

        // Remove dupes in auto
        auto = auto.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        // Final check: ensure all dependencies satisfied after auto
        foreach (var id in selected)
        {
            var def = _all.First(a => a.AgentId.Equals(id, StringComparison.OrdinalIgnoreCase));
            foreach (var dep in def.Dependencies)
                if (!selected.Contains(dep))
                    errors.Add($"{def.Name} requires {dep}");
        }

        return (errors.Count == 0, errors, auto);
    }

    public List<AgentDefinition> OrderedForExecution(IEnumerable<string> selectedIds)
    {
        var set = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);
        return _all.Where(a => set.Contains(a.AgentId)).ToList();
    }
}
