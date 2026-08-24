# AGENTS.md

## Product System

This repository is a production/product system.

Treat changes as production changes unless the user explicitly says the work is experimental, prototype, temporary, or throwaway.

Prioritize:

* Correct product behavior
* Minimal change
* Clear errors
* Strong observability
* Easy troubleshooting

---

## User Intent and Scope

Implement exactly what the user requests.

Do not expand the scope based on your own preferences or ideas.

Do not:

* refactor unrelated code
* redesign architecture
* modernize unrelated code
* fix unrelated bugs
* rename unrelated code
* introduce unnecessary abstractions
* change unrelated behavior

If the requested change can be implemented locally, prefer the local implementation.

If you identify a potentially useful unrelated improvement, do not implement it.

Mention it separately only if important.

---

## Clarification

Ask the user before implementing when clarification is required to determine:

* expected behavior
* resolution
* implementation approach
* an important business rule
* a materially different design choice

Do not invent requirements to avoid asking a question.

For obvious, low-risk changes, proceed without unnecessary questions.

---

## Implementation Guidance

For senior-engineering practices, refer to:

`.opencode/skills/senior-engineer/SKILL.md`

Do not duplicate those files' detailed instructions here.

Use the relevant guidance only when applicable.

---

---

## Build, Run, and Publish

### Build

```bash
dotnet build DrawVerify.sln
```

`DrawVerify.App` targets `net9.0-windows` (WPF) and builds on Linux via `EnableWindowsTargeting`. Test projects target `net9.0`.

### Run tests

```bash
dotnet test
```

### Rebuild / clean rebuild

Incremental rebuild:

```bash
dotnet build DrawVerify.sln --no-incremental
```

Clean rebuild:

```bash
dotnet clean DrawVerify.sln && dotnet build DrawVerify.sln
dotnet build DrawVerify.sln -t:Rebuild
```

### Publish and run the Windows app

`DrawVerify.App` is Windows-only (WPF). From WSL, publish self-contained to the path configured in `src/DrawVerify.App/appsettings.json` under `Publish.Path`, then launch it from Windows:

```bash
PUB=$(python3 -c "import json,re;p=json.load(open('src/DrawVerify.App/appsettings.json'))['Publish']['Path'];d,r=re.match(r'^([A-Za-z]):\\\\(.*)$',p).groups();print('/mnt/'+d.lower()+'/'+r.replace('\\\\','/'))") \
&& rm -rf "$PUB" \
&& dotnet restore src/DrawVerify.App -r win-x64 \
&& dotnet publish src/DrawVerify.App -c Debug -r win-x64 --self-contained true --no-restore -o "$PUB" \
&& cmd.exe /c "C:\Users\nirma\Documents\Product\DrawVerify\Published\DrawVerify.App.exe"
```

The publish destination is read from `appsettings.json` (`Publish.Path`), so changing it there changes the target.

Note: the separate `dotnet restore -r win-x64` before `--no-restore` is required — `dotnet publish -r` restores without the RuntimeIdentifier, leaving `project.assets.json` without a `net9.0-windows/win-x64` target (`NETSDK1047`).

---

This is a production system.

Prefer **clear, visible failure over silent or misleading success**.

Do not introduce silent fallback or fail-safe behavior merely to keep the application running.

Avoid:

* swallowed exceptions
* silent failures
* arbitrary default values hiding failures
* silently skipping failed operations
* pretending failed operations succeeded
* hiding dependency/integration failures

When an operation cannot be completed correctly, make the failure clear.

---

## Logging and Observability

Important errors and operational failures must remain diagnosable.

Use the existing centralized logging/monitoring mechanism.

Capture:

* the actual error/exception
* relevant operation/context
* useful diagnostic information
* correlation/request identifiers where available

Do not replace useful diagnostic information with generic errors.

User-facing errors may be concise, but detailed diagnostic information must remain available in centralized logging.

Do not log sensitive information.

### Principle

**Visible failure + useful centralized logging is preferable to silent fallback.**

---

## Graphify

`graphify-out/` is the primary codebase reference.

The code graph must be generated **only from the DrawVerify source project**:

```bash
graphify extract ./DrawVerify/src --code-only
```

Do not run Graphify against the repository root.

The graph must recursively include:

```text
./DrawVerify/src
```

and its subfolders.

When `graphify-out/graph.json` exists, use Graphify first for codebase questions:

```bash
graphify query "<question>"
graphify path "<A>" "<B>"
graphify explain "<concept>"
```

Regenerate the graph when source-code changes materially affect the code structure or dependencies.

Do not regenerate the graph unnecessarily for every agent call.

---

## Security Checks

For relevant code changes, run **Semgrep** and **Gitleaks**.

### Semgrep

```bash
mkdir -p security-reports

semgrep scan \
  --config p/default \
  --json \
  --output security-reports/semgrep.json \
  --exclude .git \
  --exclude bin \
  --exclude obj \
  --exclude node_modules \
  --exclude security-reports \
  --exclude graphify-out
```

### Gitleaks

```bash
gitleaks detect \
  --source . \
  --report-format json \
  --report-path security-reports/gitleaks.json \
  --verbose
```

The `.gitleaks.toml` config in the repo root excludes `graphify-out/` from scans (auto-discovered from `--source`). It extends the default rules so all other detections still apply.

### Security Rules
* Review all findings.
* Fix genuine code vulnerabilities, insecure patterns, and exposed secrets.
* Do not suppress findings just to make the scan pass.
* Do not expose actual secrets in agent output.
* After fixing findings, **run the scanner again**.
* Scanner execution errors must be reported clearly; never treat a failed scan as a clean scan.
* Keep reports under `security-reports/` and do not commit them.
* Use the scanner results for security decisions; do not replace them with LLM-only analysis.
