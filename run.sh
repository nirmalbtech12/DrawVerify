#!/usr/bin/env bash
set -euo pipefail

# Windows is the supported host for DrawVerify.App. Keep this file as a
# Git-Bash-compatible entry point and execute the native PowerShell workflow.
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

POWERSHELL="$(command -v pwsh || command -v powershell.exe || true)"
if [[ -z "$POWERSHELL" ]]; then
  echo "PowerShell is required. Run .\\run.ps1 from Windows PowerShell instead." >&2
  exit 1
fi

exec "$POWERSHELL" -NoProfile -ExecutionPolicy Bypass -File "$SCRIPT_DIR/run.ps1" "$@"
