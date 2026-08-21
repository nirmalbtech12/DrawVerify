// graphify OpenCode plugin
// Injects a knowledge graph reminder before bash tool calls when the graph exists.
//
// IMPORTANT: keep the reminder string free of backticks and $(...) constructs.
// The hook prepends `echo "<reminder>" && <cmd>` to the user's bash command;
// backticks inside the double-quoted echo trigger bash command substitution,
// which both corrupts tool output and silently executes the very graphify
// command we are only suggesting. Plain words render fine in opencode's TUI.
import { existsSync } from "fs";
import { join } from "path";

// On/off switch (shell env, read per session; no restart needed if the process
// inherits your shell's env):
//   GRAPHIFY_DISABLED=1   -> feature off
//   GRAPHIFY_ENABLED=0    -> feature off (alternative spelling)
//   unset / GRAPHIFY_ENABLED=1 -> feature on (still requires graphify-out/graph.json,
//                                 per the existsSync check below)
const OFF =
  isTruthy(process.env.GRAPHIFY_DISABLED) ||
  isFalsy(process.env.GRAPHIFY_ENABLED);

function isTruthy(value) {
  return value === "1" || value === "true" || value === "TRUE";
}

function isFalsy(value) {
  return value === "0" || value === "false" || value === "FALSE";
}

export const GraphifyPlugin = async ({ directory, $ }) => {
  let reminded = false;
  // Set when a file-modifying tool ran this session; cleared after an update runs.
  let dirty = false;

  if (OFF) {
    return {};
  }

  const graphPath = join(directory, "graphify-out", "graph.json");

  // AST-only incremental rebuild, no API cost. Fires once per completed turn when
  // something was changed, so the graph stays current at the end of all changes.
  const updateGraph = async () => {
    if (!dirty) return;
    dirty = false;
    if (!existsSync(graphPath)) return;
    try {
      await $.cwd(directory)`graphify update .`.nothrow().quiet();
    } catch {
      // Do not let a graph rebuild failure break the session.
    }
  };

  return {
    "tool.execute.before": async (input, output) => {
      if (reminded) return;
      if (!existsSync(graphPath)) return;

      if (input.tool === "bash") {
        // ';' not '&&' — Windows PowerShell 5.1 rejects '&&' as a statement
        // separator, breaking the first bash command of the session (#1646).
        output.args.command =
          'echo "[graphify] knowledge graph at graphify-out/. For focused questions, run graphify query with your question (scoped subgraph, usually much smaller than GRAPH_REPORT.md) instead of grepping raw files. Read GRAPH_REPORT.md only for broad architecture context." ; ' +
          output.args.command;
        reminded = true;
      }
    },
    "tool.execute.after": async (input) => {
      if (input.tool === "edit" || input.tool === "write" || input.tool === "patch") {
        dirty = true;
      }
    },
    event: async ({ event }) => {
      if (event?.type === "session.idle") {
        await updateGraph();
      }
    },
  };
};
