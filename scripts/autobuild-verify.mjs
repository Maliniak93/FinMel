// Usage: imported by autobuild.mjs - the single verify --all on master after a run that merged, reported as one line; injectable, runs nothing on import

import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import os from "node:os";
import path from "node:path";

const LINE = "verify --all na master:";

function projectOf(failure) {
  const file = String(failure.file ?? "").replaceAll("\\", "/");
  const service = /^services\/([^/]+)\//.exec(file);
  if (service) return service[1];
  const top = file.split("/")[0];
  return top || failure.step || "unknown";
}

export async function postRunVerify({ merged, dryRun, run, branch = "master", dirty = 0, log = () => {} }) {
  if (!(merged > 0) || dryRun) return null;
  if (branch !== "master" || dirty > 0) {
    const line = `${LINE} nie uruchomiono — ${branch !== "master" ? `not on master (${branch})` : `master has ${dirty} uncommitted file(s)`}`;
    log(line);
    return line;
  }
  const dir = mkdtempSync(path.join(os.tmpdir(), "skarbiec-autobuild-verify-"));
  const out = path.join(dir, "verify-result.json");
  let line;
  try {
    const outcome = run("node", ["scripts/verify.mjs", "--all", "--out", out]);
    let result;
    try {
      result = JSON.parse(readFileSync(out, "utf8"));
    } catch {
      throw new Error(outcome?.firstErr || outcome?.err || "no result file");
    }
    if (result.ok) line = `${LINE} zielone`;
    else {
      const projects = [...new Set((result.failures ?? []).map(projectOf))];
      line = `${LINE} czerwone — ${projects.length ? projects.join(", ") : "unknown"}`;
    }
  } catch (error) {
    line = `${LINE} nie uruchomiono — ${error?.message ?? error}`;
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
  log(line);
  return line;
}
