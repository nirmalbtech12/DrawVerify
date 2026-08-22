# OpenCode — model provider only

Pipeline owns agents (`.pipeline/agents/`), prompts (`.pipeline/prompts/`), schemas (`.pipeline/schemas/`).

This folder only holds OpenCode provider config (model routing via `Pipeline:ModelRouting`, `opencode` binary).

No `agents/` MD files here — pipeline is single deterministic owner.
