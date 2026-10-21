// Usage: imported by autobuild.mjs - activity labels for tool calls, background-task lines and the `still:` heartbeat tail; pure, injectable clock, runs nothing on import

const STALE_MS = 10 * 60e3;
const NO_BG_LABEL = "polecenie w tle (bez opisu)";

export const short = (text, max = 70) => {
  const flat = String(text ?? "").replace(/\s+/g, " ").trim();
  return flat.length > max ? `${flat.slice(0, max - 1)}…` : flat;
};

export const duration = (ms) => {
  const min = Math.max(0, Math.floor(ms / 60e3));
  if (min < 1) return "<1 min";
  return min < 60 ? `${min} min` : `${Math.floor(min / 60)}h ${min % 60}m`;
};

const script = (name) => new RegExp(`(?<![\\w.-])${name.replace(/\./g, "\\.")}(?![\\w-])`);
const flag = (command, name) => new RegExp(`(?<![\\w-])${name}(?![\\w-])`).test(command);

const SCRIPT_LABELS = [
  [script("ship.mjs"), "ship"],
  [script("gh-project.mjs"), "gh-project.mjs"],
  [script("preflight.mjs"), "preflight"],
  [script("stop-stack.mjs"), "stop-stack"],
];

function bashLabel(input) {
  const command = String(input.command ?? "");
  const description = short(input.description);
  if (script("e2e.mjs").test(command)) {
    const sub = /e2e\.mjs\s+(run|up|down|status|report)(?![\w-])/.exec(command)?.[1];
    if (sub && sub !== "run") return `e2e (${sub})`;
    if (flag(command, "--await")) return "czekanie na wynik e2e";
    if (flag(command, "--grep")) return "e2e (wybrane testy)";
    return sub ? "pełny zestaw e2e" : "e2e";
  }
  if (script("verify.mjs").test(command)) return flag(command, "--await") ? "czekanie na wynik weryfikacji" : "weryfikacja (verify.mjs)";
  const dotnet = /(?<![\w.-])dotnet\s+(test|build|format|ef)(?![\w-])/.exec(command)?.[1];
  if (dotnet) return `dotnet ${dotnet}`;
  if (/(?<![\w.-])(npm|npx|ng)\s/.test(command)) return "npm (web)";
  for (const [pattern, label] of SCRIPT_LABELS) if (pattern.test(command)) return label;
  if (description) return description;
  return /^\s*node(\s|$)/.test(command) ? "skrypt node (bez opisu)" : "polecenie powłoki (bez opisu)";
}

export function activityLabel(toolName, input = {}) {
  if (toolName === "Bash") return bashLabel(input ?? {});
  if (["Edit", "Write", "NotebookEdit"].includes(toolName)) return "edycja kodu";
  if (["Read", "Grep", "Glob"].includes(toolName)) return "czytanie kodu";
  return `narzędzie ${toolName}`;
}

const END_VERB = (status) => (status === "completed" ? "done" : status === "stopped" || status === "killed" ? "killed" : "failed");

export function createActivityTracker({ now }) {
  const toolLabels = new Map();
  const bgTasks = new Map();
  let activity = null;
  let snapshot = null;
  let lastGrowthAt = now();

  const onAssistant = (obj) => {
    for (const block of obj.message?.content ?? []) {
      if (block.type !== "tool_use") continue;
      const label = activityLabel(block.name, block.input);
      toolLabels.set(block.id, label);
      if (obj.parent_tool_use_id) activity = { label, at: now(), id: block.id, done: false };
    }
    return [];
  };

  const onUser = (obj) => {
    if (!obj.parent_tool_use_id || !activity) return [];
    const content = obj.message?.content;
    if (Array.isArray(content) && content.some((b) => b.type === "tool_result" && b.tool_use_id === activity.id)) activity.done = true;
    return [];
  };

  const onSystem = (obj) => {
    if (obj.subtype === "task_started" && obj.task_type === "local_bash" && obj.is_backgrounded) {
      const label = toolLabels.get(obj.tool_use_id) ?? NO_BG_LABEL;
      bgTasks.set(obj.task_id, { label, startedAt: now() });
      return [`bg: ${label}`];
    }
    if (obj.subtype === "task_notification") {
      const task = bgTasks.get(obj.task_id);
      if (!task) return [];
      bgTasks.delete(obj.task_id);
      return [`bg ${END_VERB(obj.status)}: ${task.label} · ${duration(now() - task.startedAt)}`];
    }
    return [];
  };

  const observe = (obj) => {
    if (obj?.type === "assistant") return onAssistant(obj);
    if (obj?.type === "user") return onUser(obj);
    if (obj?.type === "system") return onSystem(obj);
    return [];
  };

  const heartbeatTail = ({ tokens, tools }) => {
    const t = now();
    const grew = !snapshot || (tokens ?? 0) > snapshot.tokens || (tools ?? 0) > snapshot.tools;
    snapshot = { tokens: tokens ?? 0, tools: tools ?? 0 };
    if (grew) lastGrowthAt = t;
    else {
      if (bgTasks.size > 0) {
        const since = Math.min(...[...bgTasks.values()].map((task) => task.startedAt));
        return bgTasks.size === 1 ? `czeka na proces w tle od ${duration(t - since)}` : `czeka na procesy w tle (${bgTasks.size}) od ${duration(t - since)}`;
      }
      return `bez postępu od ${duration(t - lastGrowthAt)}`;
    }
    if (!activity) return "";
    const age = t - activity.at;
    if (activity.done && age > STALE_MS) return `teraz: brak nowych narzędzi od ${duration(age)}`;
    return `teraz: ${activity.label} (od ${duration(age)})`;
  };

  return { observe, heartbeatTail };
}
