// Usage: imported by autobuild.mjs - the CI wait loop with its rerun decision (classify failed jobs, count failed tests, rerun once per head SHA), pure and injectable; runs nothing on import

export const CI_RERUN_MAX_TESTS = 5;
export const CI_POLICY_LINE = `CI: rerun failed jobs once when < ${CI_RERUN_MAX_TESTS} tests failed`;

export function jobRef(htmlUrl) {
  const match = /\/actions\/runs\/(\d+)\/job\/(\d+)/.exec(String(htmlUrl ?? ""));
  return match ? { runId: Number(match[1]), jobId: Number(match[2]) } : null;
}

export function classifyJob(job) {
  if (["cancelled", "timed_out"].includes(job?.conclusion)) return "infra";
  const failedStep = (job?.steps ?? []).find((step) => step.conclusion === "failure");
  return failedStep && /^Test\b/.test(failedStep.name) ? "test" : "other";
}

export function countFailedTests(log) {
  const text = String(log ?? "").replace(/\x1b\[[0-9;]*m/g, "");
  let total = 0;
  for (const match of text.matchAll(/(?:Failed|Passed)!\s+-\s+Failed:\s*(\d+)/g)) total += Number(match[1]);
  const vitest = /Tests\s+(\d+) failed/.exec(text);
  if (vitest) total += Number(vitest[1]);
  return total > 0 ? total : null;
}

const names = (list) => list.map((f) => f.name).join(", ");

export function decideOnRed(failures, { attemptOf = (f) => f.attempt } = {}) {
  if (!failures.length) return { action: "stop", reason: "no failed job to rerun" };
  const other = failures.find((f) => f.kind !== "test" && f.kind !== "infra");
  if (other) return { action: "stop", reason: `${other.name} is neither a test nor an infrastructure failure` };
  const tests = failures.filter((f) => f.kind === "test");
  const unknown = tests.find((f) => !Number.isInteger(f.tests));
  if (unknown) return { action: "stop", reason: `the failed test count of ${unknown.name} is unknown` };
  const sum = tests.reduce((acc, f) => acc + f.tests, 0);
  if (sum >= CI_RERUN_MAX_TESTS) return { action: "stop", reason: `${sum} tests failed (${names(tests)}), the limit for a rerun is ${CI_RERUN_MAX_TESTS - 1}` };
  const late = failures.find((f) => !(Number.isInteger(attemptOf(f)) && attemptOf(f) < 2));
  if (late) return { action: "stop", reason: `${late.name} already ran twice` };
  const infra = failures.filter((f) => f.kind === "infra");
  const parts = [];
  if (tests.length) parts.push(`${sum} test${sum === 1 ? "" : "s"} failed (${names(tests)})`);
  if (infra.length) parts.push(`infrastructure failure (${names(infra)})`);
  return { action: "rerun", reason: parts.join(" + "), tests: sum };
}

export async function driveCi({ pr, readChecks, readJobs, readLog, rerun, sleep, say, budget, stop, onRerun = () => {}, pollMs, missingMs, timeoutMs, now = Date.now }) {
  const startedAt = now();
  let total = budget(timeoutMs);
  let missing = budget(missingMs);
  let lastStatus = "";
  let greenPolls = 0;
  let reruns = 0;
  let awaiting = [];
  let decided = null;

  async function describe(failed) {
    const jobsByRun = new Map();
    const failures = [];
    for (const check of failed) {
      const ref = jobRef(check.link);
      let job = null;
      if (ref) {
        if (!jobsByRun.has(ref.runId)) jobsByRun.set(ref.runId, await readJobs(ref.runId));
        job = (jobsByRun.get(ref.runId) ?? []).find((j) => j.id === ref.jobId) ?? null;
      }
      const kind = job ? classifyJob(job) : "other";
      const tests = kind === "test" ? countFailedTests(await readLog(ref.jobId)) : null;
      failures.push({ name: check.name, link: check.link, runId: ref?.runId ?? null, kind, tests, attempt: job?.run_attempt ?? null });
    }
    return failures;
  }

  async function decide(failed) {
    const key = failed.map((c) => c.link).sort().join("|");
    if (decided?.key !== key) {
      const failures = await describe(failed);
      decided = { key, failures, decision: decideOnRed(failures) };
    }
    return decided;
  }

  // false: the checks read may still be the old result (also right after the attempt rose), so read them again after a pause
  async function attemptStarted(checks) {
    if (checks.some((c) => c.bucket === "pending")) {
      awaiting = [];
      return true;
    }
    const still = [];
    for (const wait of awaiting) {
      const jobs = await readJobs(wait.runId);
      if (!jobs?.some((j) => j.run_attempt > wait.attempt)) still.push(wait);
    }
    awaiting = still;
    return false;
  }

  async function startRerun(failures, decision) {
    const attempts = new Map();
    for (const f of failures) attempts.set(f.runId, Math.max(attempts.get(f.runId) ?? 0, f.attempt));
    say(`CI PR #${pr.number}: ${decision.reason} — rerun failed jobs`);
    for (const runId of attempts.keys()) {
      if (!(await rerun(runId))) throw stop("ci-failed", `PR ${pr.url}: rerun of run ${runId} failed (${decision.reason})`);
    }
    reruns++;
    onRerun();
    awaiting = [...attempts].map(([runId, attempt]) => ({ runId, attempt }));
    total = budget(timeoutMs);
    missing = null;
    decided = null;
    greenPolls = 0;
    lastStatus = "";
  }

  while (true) {
    const checks = await readChecks();
    if (!checks) {
      greenPolls = 0;
      if (total.expired()) throw stop("ci-timeout", `PR ${pr.url}: checks not readable or not green after 90 min`);
      await sleep(pollMs);
      continue;
    }
    if (checks.length) missing = null;
    else missing ??= budget(missingMs);

    if (awaiting.length && !(await attemptStarted(checks))) {
      if (total.expired()) throw stop("ci-timeout", `PR ${pr.url}: the rerun did not start within 90 min`);
      await sleep(pollMs);
      continue;
    }

    const count = (...buckets) => checks.filter((c) => buckets.includes(c.bucket)).length;
    const failed = checks.filter((c) => ["fail", "cancel"].includes(c.bucket));
    const status = `pass ${count("pass")} · skipping ${count("skipping")} · pending ${count("pending")} · fail ${failed.length}`;
    if (checks.length && status !== lastStatus) say(`CI PR #${pr.number}: ${status}`);
    lastStatus = status;

    if (failed.length) {
      const { failures, decision } = await decide(failed);
      if (decision.action === "stop") throw stop("ci-failed", `PR ${pr.url}: ${failed.map((c) => `${c.name} (${c.link})`).join(", ")} — ${decision.reason}`);
      if (!count("pending")) {
        await startRerun(failures, decision);
        await sleep(pollMs);
        continue;
      }
    }
    if (missing?.expired()) throw stop("ci-missing", `PR ${pr.url}: no checks after 10 min - the runner did not pick up the job`);
    greenPolls = checks.length && checks.every((c) => ["pass", "skipping"].includes(c.bucket)) ? greenPolls + 1 : 0;
    // two green polls in a row: a late-registered check must not slip past the merge
    if (greenPolls >= 2) return { ms: now() - startedAt, reruns };
    if (total.expired()) throw stop("ci-timeout", `PR ${pr.url}: checks not green after 90 min`);
    await sleep(pollMs);
  }
}

const prNumber = (url) => url?.split("/").pop();

export const ciCell = (minutes, reruns) => `CI ${minutes} min${reruns ? `, CI rerun ${reruns}` : ""}`;

export const partLine = (p) => `#${p.number} PR #${prNumber(p.prUrl)} (${p.shipOnly ? "ship retried" : `build ${Math.round(p.buildMin ?? 0)} min`}, ${ciCell(Math.round(p.ciMin ?? 0), p.ciReruns)})`;

export const summaryPartLine = (p) => `- #${p.number} ${p.title} - ${p.prUrl ?? "no PR"} (${p.shipOnly ? "ship retried" : `build ${p.buildMin ?? "-"} min`}, ${ciCell(p.ciMin ?? "-", p.ciReruns)})`;
