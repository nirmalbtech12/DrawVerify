using LEMUAgenticPipeline.Web.Models;
using LEMUAgenticPipeline.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class RunModel : PageModel
{
    private readonly AgentRegistry _registry;
    private readonly PipelineScheduler _scheduler;
    public RunModel(AgentRegistry registry, PipelineScheduler scheduler) { _registry = registry; _scheduler = scheduler; }

    public List<AgentDefinition> Agents { get; set; } = [];
    public List<string> PreviewIds { get; set; } = [];
    public List<string> AutoSelected { get; set; } = [];

    [BindProperty] public RunInput Input { get; set; } = new();

    public class RunInput
    {
        public string JiraIdsRaw { get; set; } = string.Empty;
        public ScheduleType ScheduleType { get; set; } = ScheduleType.Immediate;
        public string? OneTimeTime { get; set; } // HH:mm
        public List<string> SelectedAgentIds { get; set; } = [];
        public bool StopOnFailure { get; set; } = true;
        public DateTime? OneTimeAt
        {
            get
            {
                if (string.IsNullOrWhiteSpace(OneTimeTime)) return null;
                if (TimeSpan.TryParse(OneTimeTime, out var ts)) return DateTime.Today.Add(ts);
                return null;
            }
        }
    }

    public void OnGet()
    {
        Agents = _registry.All.ToList();
        Input.SelectedAgentIds = _registry.DefaultPipeline.Select(a => a.AgentId).ToList();
        PreviewIds = PipelineScheduler.ParseJiraIds(Input.JiraIdsRaw ?? "");
    }

    public IActionResult OnPostDefault()
    {
        Agents = _registry.All.ToList();
        Input.SelectedAgentIds = _registry.DefaultPipeline.Select(a => a.AgentId).ToList();
        PreviewIds = PipelineScheduler.ParseJiraIds(Input.JiraIdsRaw ?? "");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Agents = _registry.All.ToList();
        PreviewIds = PipelineScheduler.ParseJiraIds(Input.JiraIdsRaw ?? "");

        var (valid, errs, auto) = _registry.ValidateSelection(Input.SelectedAgentIds);
        AutoSelected = auto;
        // Auto-select deps for preview
        var finalIds = Input.SelectedAgentIds.Union(auto, StringComparer.OrdinalIgnoreCase).ToList();

        var req = new CreatePipelineRequest
        {
            JiraIdsRaw = Input.JiraIdsRaw,
            ScheduleType = Input.ScheduleType,
            OneTimeAt = Input.OneTimeAt,
            SelectedAgentIds = finalIds,
            StopOnFailure = Input.StopOnFailure
        };
        var (ok, errors) = await _scheduler.ValidateAsync(req);
        foreach (var e in errors) ModelState.AddModelError("", e);
        if (!valid) foreach (var e in errs) ModelState.AddModelError("", e);
        if (!ModelState.IsValid) return Page();

        var schedule = await _scheduler.CreateScheduleAsync(req, User.Identity?.Name ?? "web");
        return RedirectToPage("/Pipeline/Runs/Index");
    }
}
