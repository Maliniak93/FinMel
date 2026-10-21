// Usage: imported by autobuild.mjs - decides and runs the test-time measurement after an epic closes, reported as one line; injectable, runs nothing on import

const LINE = "pomiar czasów testów:";
const TEST_ROOTS = ["services/", "contracts/", "gateway/", "web/", "Skarbiec.ServiceDefaults/", "Skarbiec.Testing/"];
export const MEASURE_ARGS = ["scripts/test-timings.mjs", "--from-autobuild", "--wait", "15", "--timeout", "45"];
const CHILD_LIMIT_MS = 70 * 60e3;

export function touchesTests(paths) {
  return paths.some((raw) => {
    const file = String(raw).replaceAll("\\", "/");
    if (file.startsWith("e2e/")) return false;
    return TEST_ROOTS.some((root) => file.startsWith(root)) || file.split("/").some((segment) => segment.endsWith(".Tests")) || file.endsWith(".spec.ts");
  });
}

function mergedPulls({ branch, gh, repo }) {
  const owner = repo.split("/")[0];
  const found = gh(["api", `repos/${repo}/pulls?state=closed&head=${encodeURIComponent(`${owner}:${branch}`)}`, "--jq", ".[] | select(.merged_at != null) | .number"]);
  if (!found.ok) throw new Error(`pulls of ${branch}: ${found.firstErr}`);
  return found.out.split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
}

export function collectEpicFiles({ parts, branchOf, gh, repo }) {
  const files = new Set();
  for (const part of parts) {
    const branch = branchOf(part);
    if (!branch) throw new Error(`#${part} has no Branch`);
    const pulls = mergedPulls({ branch, gh, repo });
    if (!pulls.length) throw new Error(`#${part} has no merged PR on ${branch}`);
    for (const pull of pulls) {
      const listed = gh(["api", `repos/${repo}/pulls/${pull}/files`, "--paginate", "--jq", ".[].filename"]);
      if (!listed.ok) throw new Error(`files of PR ${pull}: ${listed.firstErr}`);
      for (const file of listed.out.split(/\r?\n/).map((l) => l.trim()).filter(Boolean)) files.add(file);
    }
  }
  return [...files].sort();
}

function detailOf(out) {
  const lines = String(out ?? "").split(/\r?\n/).map((l) => l.trim());
  return lines.find((l) => /^(Razem|Nic nie zmierzono)/.test(l)) ?? lines.find((l) => /^(działa:|Niezmierzony:|Docker nie)/.test(l)) ?? "";
}

function outcomeText(result) {
  const detail = detailOf(result?.out);
  const tail = detail ? ` — ${detail}` : "";
  if (result?.ok) return `zakończony${tail}`;
  if (result?.code === 1) return `zakończony, są czerwone testy (kod 1)${tail}`;
  if (result?.code === 3) return `niepełny (kod 3: maszyna zajęta, limit czasu lub brak Dockera)${tail}`;
  return `nie powiódł się — ${result?.error?.message ?? (result?.firstErr || `kod ${result?.code ?? "brak"}`)}`;
}

export async function measureAfterEpic({ number, epic, dryRun, branch = "master", dirty = 0, files, run, log = () => {} }) {
  const emit = (text) => {
    const line = `${LINE} #${number} ${text}`;
    log(line);
    return line;
  };
  if (!epic) return emit("pominięty — to nie epik");
  if (dryRun) return emit("pominięty — dry-run (po epiku sprawdzę pliki jego PR-ów i zmierzę, gdy dotkną testów lub logiki)");
  if (branch !== "master" || dirty > 0) return emit(`pominięty — ${branch !== "master" ? `nie na masterze (${branch})` : `master ma ${dirty} niezatwierdzonych plików`}`);
  try {
    const changed = files();
    if (!touchesTests(changed)) return emit(`pominięty — epik nie zmienił testów ani logiki (${changed.length} plików)`);
    const result = run("node", MEASURE_ARGS, { timeout: CHILD_LIMIT_MS });
    return emit(outcomeText(result));
  } catch (error) {
    return emit(`nie powiódł się — ${error?.message ?? error}`);
  }
}
