# Pipeline (deterministic owner)

Single source for agentic pipeline:

- `agents/` — agent definitions (technical-designer, test-designer, implementer, verifier, reviewer)
- `prompts/` — task prompts per agent + `system.md`
- `schemas/` — JSON schemas for agent outputs
- `PipelineOrchestrator/` — C# orchestrator (state machine, ICodeAgentService)
- `state/<JIRA-ID>/` — per-story artifacts

`.opencode/` is model provider only (no agents). `Pipeline:Paths:AgentDir=.pipeline/agents`.

## OpenCode Server (Session Reuse)

To avoid MCP cold starts on each `opencode run` invocation, you can run a long-running OpenCode server:

```bash
# Start server (runs in foreground; use a process manager for production)
opencode serve --port 4096
```

Then configure the pipeline to attach to it via `appsettings.json` or environment variable:

```json
{
  "Pipeline": {
    "OpenCode": {
      "AttachUrl": "http://localhost:4096"
    }
  }
}
```

Or via environment variable:
```bash
export Pipeline__OpenCode__AttachUrl=http://localhost:4096
```

When `AttachUrl` is set, the pipeline uses `--attach http://localhost:4096` with `opencode run`, reusing the server session instead of starting a new one each time. If not set, runs standalone `opencode run` as before (backward-compatible).
