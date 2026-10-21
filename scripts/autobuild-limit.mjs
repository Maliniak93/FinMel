// Usage: imported by autobuild.mjs - what to do when a build hits the Claude usage limit, readiness after the wait, the end-of-run shutdown; pure, injectable, runs nothing on import

const MIN = 60e3;
const WAKE_AFTER_RESET_MS = 5 * MIN;
const MAX_AWAKE_WAIT_MS = 15 * MIN;
const HIBERNATE_FROM_MS = 60 * MIN;

export function decideOnLimit({ resetsAt, now, onLimit, maxWaitHours, timersWork, hibernateAvailable, windowEnd }) {
  if (onLimit === "exit") return { action: "end", until: null, reason: "limit" };
  if (!resetsAt) return { action: "end", until: null, reason: "limit-unknown" };
  const until = new Date(resetsAt.getTime() + WAKE_AFTER_RESET_MS);
  const wait = until.getTime() - now.getTime();
  if (wait > maxWaitHours * 60 * MIN) return { action: "end", until, reason: "limit-too-far" };
  if (windowEnd && until.getTime() >= windowEnd.getTime()) return { action: "end", until, reason: "window" };
  if (onLimit === "wait") return { action: "wait", until, reason: "wait" };
  if (!timersWork) return { action: "wait", until, reason: "no-timers" };
  if (wait <= MAX_AWAKE_WAIT_MS) return { action: "wait", until, reason: "short" };
  if (wait >= HIBERNATE_FROM_MS && hibernateAvailable) return { action: "hibernate", until, reason: "sleep" };
  return { action: "suspend", until, reason: "sleep" };
}

const hhmm = (date) => date.toTimeString().slice(0, 5);
const VERB = { hibernate: "hibernacja do", suspend: "uśpienie do", wait: "czekam do" };

export async function handleLimit(input, { confirm, suspendUntil, waitUntil, log }) {
  const decision = decideOnLimit(input);
  if (decision.action === "end") return decision;
  const { action, until } = decision;
  log(`limit Claude, reset ${hhmm(input.resetsAt)} — ${VERB[action]} ${hhmm(until)}`);
  if (action === "wait") {
    await waitUntil(until);
    return decision;
  }
  const answer = await confirm(action);
  if (answer === "no") {
    log(`${action === "hibernate" ? "hibernacja" : "uśpienie"} odrzucone - czekam do ${hhmm(until)}`);
    await waitUntil(until);
  } else await suspendUntil(until, action);
  return decision;
}

export async function awaitReady({ check, now, sleep, timeoutMs = 15 * MIN, retryMs = 30e3 }) {
  const startedAt = now();
  while (true) {
    if (await check()) return { ready: true };
    if (now() - startedAt >= timeoutMs) return { ready: false, reason: "wake-not-ready" };
    await sleep(retryMs);
  }
}

// the 2-minute unattended-sleep window starts at the wake, so the guard is confirmed before anything else runs; one retry, then the run goes on with a warning
export async function rearmGuard({ start, log, line }) {
  let guard = await start();
  if (!guard.active) {
    log(`blokada snu: ${guard.reason} - próbuję ponownie`);
    guard = await start();
  }
  log(line(guard));
  return guard;
}

// a rebuild after the wait is the same part again: ctx.builds is not touched
export async function buildWithResume({ part, build, onLimit, rearm, awaitReady: ready }) {
  while (true) {
    const result = await build(part);
    if (!result.limit) return result;
    await onLimit(result.limit);
    await rearm?.();
    await ready();
  }
}

// a fix session cut by the usage limit runs again with the same prompt on the working tree as it is
export async function fixWithResume({ prompt, run, onLimit, rearm, awaitReady: ready }) {
  while (true) {
    const result = await run(prompt);
    if (!result.limitHit || result.timedOut || result.hung) return result;
    await onLimit({ resetsAt: result.resetsAt });
    await rearm?.();
    await ready();
  }
}

export async function finishShutdown({ confirm, run, noShutdown, dryRun, log = () => {} }) {
  if (noShutdown || dryRun) return "skipped";
  const answer = await confirm("shutdown");
  if (answer === "no") {
    log("shutdown declined - the PC stays on");
    return answer;
  }
  log(`shutting down now (${answer === "yes" ? "confirmed" : "no answer in 10 min"})`);
  run("shutdown", ["/s", "/t", "0"]);
  return answer;
}
