// Usage: imported by autobuild.mjs - the queue plan, the next piece of work, the start and window times; pure, injectable, runs nothing on import

const partNumbers = (epic) => (epic.subIssues ?? []).map((s) => (typeof s === "object" ? s.number : s));
const TIME_RE = /^(\d{1,2}):(\d{2})$/;

export function planFrom(queue, { only, includeManual = false, items = Infinity } = {}, cards = queue) {
  if (only !== undefined && only !== null) return planOne(queue, Number(only), cards);
  return queue.filter((q) => includeManual || !q.manual).slice(0, items);
}

function planOne(queue, number, cards) {
  const queued = queue.find((q) => q.number === number);
  if (queued) return [queued];
  const card = cards.find((c) => c.number === number);
  const parent = cards.find((c) => c.epic && partNumbers(c).includes(number))?.number ?? card?.parent;
  if (parent != null) throw new Error(`#${number} is a part of epic #${parent}`);
  if (!card) throw new Error(`#${number} is not on the board`);
  return [];
}

export function nextWork(item, cards) {
  if (!item.epic) return item.number;
  const byNumber = new Map(cards.map((c) => [c.number, c]));
  const next = partNumbers(item)
    .map((n) => byNumber.get(n))
    .find((p) => p && p.status !== "Done");
  return next ? next.number : null;
}

function nextOccurrence(hhmm, now) {
  const match = TIME_RE.exec(String(hhmm));
  if (!match || Number(match[1]) > 23 || Number(match[2]) > 59) throw new Error(`"${hhmm}" is not a time: use HH:MM`);
  const at = new Date(now);
  at.setHours(Number(match[1]), Number(match[2]), 0, 0);
  if (at.getTime() <= now.getTime()) at.setDate(at.getDate() + 1);
  return at;
}

export function startAt(spec, now) {
  if (spec === undefined || spec === "now") return new Date(now);
  if (/^\d+$/.test(String(spec))) return new Date(now.getTime() + Number(spec) * 60e3);
  return nextOccurrence(spec, now);
}

export const windowEnd = (hhmm, now) => nextOccurrence(hhmm, now);

export const mayStart = (until, now) => (until && now.getTime() >= until.getTime() ? "window" : null);

export async function startWork({ item, part, gh, build }) {
  for (const number of new Set([item, part])) await gh(["issue", "edit", String(number), "--add-label", "autobuild"]);
  return build();
}
