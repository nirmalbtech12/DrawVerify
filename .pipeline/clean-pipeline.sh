#!/usr/bin/env bash
set -euo pipefail
# clean-pipeline.sh - Truncate all LEMU pipeline tables for a clean refresh
#
# Cleans EF Core SQLite store used by LEMUAgenticPipeline.Web:
#   PipelineLogs, PipelineRunAgents, PipelineRuns, PipelineScheduleItems, PipelineSchedules
# Preserves schema + __EFMigrationsHistory/__EFMigrationsLock, resets sqlite_sequence, VACUUMs.
#
# DB locations (auto-discovered):
#   .pipeline/LEMUAgenticPipeline.Web/pipeline.db  (authoritative - appsettings ConnectionStrings:Pipeline)
#   ./pipeline.db                                   (legacy root - cleaned if present)
#
# State artifacts (.pipeline/state/<JIRA>/*.md, *-approval.json, *-prompt.md) are NOT
# cleared by default. Use --with-state for a full refresh.
#
# Usage:
#   .pipeline/clean-pipeline.sh                    # truncate tables
#   .pipeline/clean-pipeline.sh --dry-run          # show counts, do not delete
#   .pipeline/clean-pipeline.sh --with-state       # also wipe .pipeline/state/*
#   .pipeline/clean-pipeline.sh --db <path>        # target single DB
#   ./clean-pipeline.sh from repo root also works (path resolution is relative to script)

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

DRY_RUN=0
WITH_STATE=0
DB_OVERRIDE=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --dry-run) DRY_RUN=1; shift ;;
    --with-state) WITH_STATE=1; shift ;;
    --db)
      if [[ $# -lt 2 ]]; then echo "error: --db requires a path" >&2; exit 1; fi
      DB_OVERRIDE="$2"; shift 2 ;;
    --db=*) DB_OVERRIDE="${1#--db=}"; shift ;;
    -h|--help)
      cat <<'HELP'
Usage: clean-pipeline.sh [options]

Options:
  --dry-run      Show row counts, do not delete
  --with-state   Also wipe .pipeline/state/* artifacts
  --db <path>    Target single DB file (default: auto-discover both)
  -h, --help     Show help

Examples:
  .pipeline/clean-pipeline.sh --dry-run
  .pipeline/clean-pipeline.sh --with-state
  .pipeline/clean-pipeline.sh --db .pipeline/LEMUAgenticPipeline.Web/pipeline.db
HELP
      exit 0 ;;
    *) echo "unknown arg: $1 (try --help)" >&2; exit 1 ;;
  esac
done

# Resolve DB candidates absolute so script works from any cwd
CANDIDATES=()
if [[ -n "$DB_OVERRIDE" ]]; then
  if [[ "$DB_OVERRIDE" = /* ]]; then
    CANDIDATES+=("$DB_OVERRIDE")
  else
    # relative to repo root if not absolute
    CANDIDATES+=("$REPO_ROOT/$DB_OVERRIDE")
    # also try as given relative to cwd
    [[ -f "$DB_OVERRIDE" ]] && CANDIDATES+=("$(cd "$(dirname "$DB_OVERRIDE")" && pwd)/$(basename "$DB_OVERRIDE")")
  fi
else
  CANDIDATES+=("$SCRIPT_DIR/LEMUAgenticPipeline.Web/pipeline.db")
  CANDIDATES+=("$REPO_ROOT/.pipeline/LEMUAgenticPipeline.Web/pipeline.db")
  CANDIDATES+=("$REPO_ROOT/pipeline.db")
fi

# Deduplicate and keep existing files only
DBS=()
seen=""
for c in "${CANDIDATES[@]}"; do
  # normalize path (remove duplicate slashes, resolve .. without requiring existence)
  nc="$(realpath -m "$c" 2>/dev/null || echo "$c")"
  if [[ "$seen" == *"|$nc|"* ]]; then continue; fi
  seen+="|$nc|"
  if [[ -f "$nc" ]]; then
    DBS+=("$nc")
  fi
done

if [[ ${#DBS[@]} -eq 0 ]]; then
  echo "No pipeline.db found." >&2
  echo "Searched:" >&2
  for c in "${CANDIDATES[@]}"; do echo "  $c" >&2; done
  echo "Hint: check ConnectionStrings:Pipeline in .pipeline/LEMUAgenticPipeline.Web/appsettings.json" >&2
  exit 1
fi

# Deduplicate DBs that are the same inode (symlink cases)
if [[ ${#DBS[@]} -gt 1 ]]; then
  declare -A by_inode=()
  uniq=()
  for d in "${DBS[@]}"; do
    ino="$(stat -c '%d:%i' "$d" 2>/dev/null || echo "$d")"
    if [[ -z "${by_inode[$ino]+x}" ]]; then
      by_inode["$ino"]=1
      uniq+=("$d")
    fi
  done
  DBS=("${uniq[@]}")
fi

clean_one_db() {
  local db="$1"
  echo ""
  echo "=== DB: $db ==="
  if [[ ! -f "$db" ]]; then
    echo "  not found, skipping"
    return 0
  fi

  # Prefer sqlite3 if installed, else python3 sqlite3 module (always available, no extra deps)
  if command -v sqlite3 >/dev/null 2>&1; then
    echo "Counts before:"
    if ! sqlite3 "$db" "SELECT ' PipelineLogs='||(SELECT COUNT(*) FROM PipelineLogs)||' PipelineRunAgents='||(SELECT COUNT(*) FROM PipelineRunAgents)||' PipelineRuns='||(SELECT COUNT(*) FROM PipelineRuns)||' PipelineScheduleItems='||(SELECT COUNT(*) FROM PipelineScheduleItems)||' PipelineSchedules='||(SELECT COUNT(*) FROM PipelineSchedules);" 2>&1; then
      echo "  (failed to query - DB may be locked or not a pipeline DB)" >&2
      return 1
    fi
    if [[ $DRY_RUN -eq 1 ]]; then
      echo "[dry-run] skipping DELETE"
      return 0
    fi
    if ! sqlite3 "$db" <<'SQL'
PRAGMA foreign_keys=OFF;
BEGIN TRANSACTION;
DELETE FROM PipelineLogs;
DELETE FROM PipelineRunAgents;
DELETE FROM PipelineScheduleItems;
DELETE FROM PipelineRuns;
DELETE FROM PipelineSchedules;
DELETE FROM sqlite_sequence WHERE name IN ('PipelineLogs','PipelineRunAgents','PipelineRuns','PipelineScheduleItems','PipelineSchedules');
COMMIT;
VACUUM;
SQL
    then
      echo "  ERROR: sqlite3 DELETE failed (DB locked? stop web with: fuser -k 5000/tcp; pkill -f LEMUAgenticPipeline.Web)" >&2
      return 1
    fi
    echo "Counts after:"
    sqlite3 "$db" "SELECT ' PipelineLogs='||(SELECT COUNT(*) FROM PipelineLogs)||' PipelineRunAgents='||(SELECT COUNT(*) FROM PipelineRunAgents)||' PipelineRuns='||(SELECT COUNT(*) FROM PipelineRuns)||' PipelineScheduleItems='||(SELECT COUNT(*) FROM PipelineScheduleItems)||' PipelineSchedules='||(SELECT COUNT(*) FROM PipelineSchedules);"
    echo "✓ $db cleaned (VACUUM done, kept __EFMigrationsHistory)"
  else
    python3 - "$db" "$DRY_RUN" <<'PY'
import sqlite3, sys
db, dry = sys.argv[1], sys.argv[2]=="1"
con = sqlite3.connect(db, timeout=10)
con.execute("PRAGMA busy_timeout=5000")
cur = con.cursor()
tables = ["PipelineLogs","PipelineRunAgents","PipelineRuns","PipelineScheduleItems","PipelineSchedules"]
print("Counts before:")
for t in tables:
    try:
        cur.execute(f'SELECT COUNT(*) FROM "{t}"')
        print(f"  {t}: {cur.fetchone()[0]} rows")
    except Exception as e:
        print(f"  {t}: missing ({e})")
        sys.exit(1)
if dry:
    print("[dry-run] skipping DELETE")
    sys.exit(0)
try:
    cur.execute("PRAGMA foreign_keys=OFF")
    cur.executescript("""
BEGIN TRANSACTION;
DELETE FROM PipelineLogs;
DELETE FROM PipelineRunAgents;
DELETE FROM PipelineScheduleItems;
DELETE FROM PipelineRuns;
DELETE FROM PipelineSchedules;
DELETE FROM sqlite_sequence WHERE name IN ('PipelineLogs','PipelineRunAgents','PipelineRuns','PipelineScheduleItems','PipelineSchedules');
COMMIT;
VACUUM;
""")
    con.commit()
except Exception as e:
    print(f"ERROR: {e}", file=sys.stderr)
    print("Hint: DB may be locked - stop web: fuser -k 5000/tcp; pkill -f LEMUAgenticPipeline.Web", file=sys.stderr)
    sys.exit(1)
print("Counts after:")
for t in tables:
    cur.execute(f'SELECT COUNT(*) FROM "{t}"')
    print(f"  {t}: {cur.fetchone()[0]} rows")
con.close()
print("✓ cleaned (VACUUM done, kept __EFMigrationsHistory)")
PY
    py_rc=$?
    if [[ $py_rc -ne 0 ]]; then
      echo "  ERROR: python cleanup failed" >&2
      return 1
    fi
    if [[ $DRY_RUN -eq 1 ]]; then
      return 0
    fi
    echo "✓ $db cleaned"
  fi

  # Remove WAL/SHM if VACUUM left them (harmless, but keeps dir tidy)
  if [[ $DRY_RUN -eq 0 ]]; then
    rm -f "$db-wal" "$db-shm" 2>/dev/null || true
  fi
}

FAILED=0
for db in "${DBS[@]}"; do
  if ! clean_one_db "$db"; then
    FAILED=1
  fi
done

STATE_DIR="$REPO_ROOT/.pipeline/state"
if [[ $WITH_STATE -eq 1 ]]; then
  echo ""
  echo "=== .pipeline/state ==="
  if [[ -d "$STATE_DIR" ]]; then
    if [[ $DRY_RUN -eq 1 ]]; then
      echo "[dry-run] would remove:"
      find "$STATE_DIR" -mindepth 1 -maxdepth 2 -print | head -n 50
      [[ $(find "$STATE_DIR" -mindepth 1 | wc -l) -gt 50 ]] && echo "  ... (truncated)"
    else
      echo "Removing $STATE_DIR/* (preserving directory)..."
      # keep state dir itself, remove all children
      find "$STATE_DIR" -mindepth 1 -exec rm -rf {} +
      echo "✓ state cleared: $STATE_DIR is now empty"
    fi
  else
    echo "no state dir at $STATE_DIR"
  fi
else
  echo ""
  echo "Note: .pipeline/state artifacts NOT cleared"
  if [[ -d "$STATE_DIR" ]] && [[ $(find "$STATE_DIR" -mindepth 1 2>/dev/null | wc -l) -gt 0 ]]; then
    echo "  Found $(find "$STATE_DIR" -mindepth 1 | wc -l) file(s) under $STATE_DIR (e.g., KAN-*/technical-design.md, *-approval.json)"
    echo "  Re-run with --with-state for a full refresh:"
    echo "    .pipeline/clean-pipeline.sh --with-state"
  fi
fi

echo ""
if [[ $DRY_RUN -eq 1 ]]; then
  echo "Dry-run done. Re-run without --dry-run to apply."
else
  if [[ $FAILED -ne 0 ]]; then
    echo "Completed with errors (some DBs may still be locked). Stop the web and retry." >&2
    echo "  fuser -k 5000/tcp; pkill -f LEMUAgenticPipeline.Web; .pipeline/clean-pipeline.sh" >&2
    exit 1
  fi
  echo "Done. Restart pipeline:"
  echo "  .pipeline/run.sh"
  echo "  # or: dotnet run --project .pipeline/LEMUAgenticPipeline.Web --urls http://localhost:5000"
  echo "  curl http://localhost:5000/health"
fi
