using Microsoft.AspNetCore.SignalR;

namespace LEMUAgenticPipeline.Web.Hubs;

public sealed class PipelineHub : Hub
{
    public Task JoinRun(string runId) => Groups.AddToGroupAsync(Context.ConnectionId, $"run-{runId}");
    public Task LeaveRun(string runId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"run-{runId}");
}
