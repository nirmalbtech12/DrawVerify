namespace LEMUAgenticPipeline.Web.Models;

public sealed class AgentDefinition
{
    public string AgentId { get; set; } = string.Empty; // technical-design
    public string Name { get; set; } = string.Empty; // Technical Design Agent
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int ExecutionOrder { get; set; }
    public List<string> Dependencies { get; set; } = []; // AgentIds required
    public bool RequiresHumanApproval { get; set; }
}

public static class DefaultAgents
{
    // Determined from existing pipeline (§4 STATE_MACHINE + agent md). Config-driven, not hard-coded in UI.
    public static readonly List<AgentDefinition> All = [
        new AgentDefinition { AgentId="technical-design", Name="Technical Design", Description="Produces implementation-ready technical design", ExecutionOrder=1, Dependencies=[], RequiresHumanApproval=true, Enabled=true },
        new AgentDefinition { AgentId="test-design", Name="Test Design", Description="Maps ACs to unit/integration/edge tests", ExecutionOrder=2, Dependencies=["technical-design"], RequiresHumanApproval=true, Enabled=true },
        new AgentDefinition { AgentId="implementation", Name="Implementation", Description="Implements approved designs (minimal changes)", ExecutionOrder=3, Dependencies=["technical-design","test-design"], RequiresHumanApproval=false, Enabled=true },
        new AgentDefinition { AgentId="test", Name="Test", Description="Runs dotnet build + dotnet test", ExecutionOrder=4, Dependencies=["implementation"], RequiresHumanApproval=false, Enabled=true },
        new AgentDefinition { AgentId="code-review", Name="Code Review", Description="Read-only review vs ACs/designs/diff", ExecutionOrder=5, Dependencies=["test"], RequiresHumanApproval=false, Enabled=true },
        new AgentDefinition { AgentId="jira-update", Name="Jira Update", Description="Updates Jira + creates PR (orchestrator-only)", ExecutionOrder=6, Dependencies=["code-review"], RequiresHumanApproval=false, Enabled=false },
    ];
}
