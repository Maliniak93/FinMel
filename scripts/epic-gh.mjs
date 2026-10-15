// Usage: imported by epic.mjs - the GitHub-limit wait and the idempotent-write helpers, pure and injectable; runs nothing on import

import { GhLimitError, hasRunMarker, limitFromText, limitWait } from "./gh.mjs";

const defaultSleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

export function detectChildLimit({ output }) {
  return limitFromText(output);
}

export function limitFromStreamEvent(event) {
  if (event?.type !== "user") return null;
  const content = event.message?.content;
  const parts = Array.isArray(content) ? content : [{ content }];
  const text = parts
    .flatMap((part) => (Array.isArray(part.content) ? part.content : [part.content]))
    .map((item) => (typeof item === "string" ? item : (item?.text ?? "")))
    .join("\n");
  return limitFromText(text);
}

export async function runStepWithLimit(step, { log = console.log, sleep = defaultSleep, now = Date.now, onWait } = {}) {
  for (let attempt = 1; ; attempt++) {
    const result = await step();
    const limit = detectChildLimit(result);
    if (!limit) return result;
    const { ms, until } = limitWait(new GhLimitError(limit.kind, limit.resetAt), attempt, now());
    log(`limit GitHuba (${limit.kind}), czekam do ${until}`);
    onWait?.(ms);
    await sleep(ms);
    log("limit GitHuba: wznawiam ten sam krok");
  }
}

export function createTimeoutBudget(timeoutMs, nowFn = Date.now) {
  const startedAt = nowFn();
  let excluded = 0;
  return {
    exclude: (ms) => {
      excluded += ms;
    },
    expired: () => nowFn() - startedAt - excluded > timeoutMs,
  };
}

export async function mergeUnlessMerged({ state, merge }) {
  if ((await state()) === "MERGED") return "skipped";
  await merge();
  return "done";
}

export async function closeUnlessClosed({ state, close }) {
  if ((await state()) === "CLOSED") return "skipped";
  await close();
  return "done";
}

export async function postOnce({ comments, marker, body = "", post }) {
  if (hasRunMarker(await comments(), marker)) return "skipped";
  await post(body ? `${body}\n\n${marker}` : marker);
  return "done";
}
