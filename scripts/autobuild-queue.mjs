// Usage: imported by autobuild.mjs - the queue plan, the next piece of work, the start and window times; pure, injectable, runs nothing on import

import { postOnce } from "./autobuild-gh.mjs";
import { shortOf } from "./priority.mjs";
import { itemTag, queueOf } from "./queue.mjs";

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

export function planLines(plan, cards) {
  return plan.map((item, i) => {
    const next = nextWork(item, cards);
    const action = item.epic ? (next ? `next #${next}` : "no open part") : "standalone";
    return `${i + 1}. #${item.number} ${item.title}${item.manual ? " (ręczny)" : ""} — ${action} [${itemTag(item)}]`;
  });
}

export function pickNext(queue, { started = new Set(), items = Infinity, includeManual = false } = {}) {
  return (
    queue.find((q) => {
      if (q.manual && !includeManual) return false;
      if (started.has(q.number)) return !q.epic || q.next !== null;
      return started.size < items;
    }) ?? null
  );
}

export const tierProblem = (card) => (["1", "2"].includes(String(card?.tier ?? "")) ? null : "brak Tier");

export const skipMarker = (number) => `<!-- skarbiec-autobuild-skip:${number} -->`;

export const skippedLine = (skipped) => (skipped.length ? `pominięte: ${skipped.map((s) => `#${s.number} (${s.reason})`).join(", ")}` : null);

export function ensureBranch(part, heal) {
  if (part.branch) return part;
  const { branch } = heal(part.number);
  if (!branch) throw new Error(`#${part.number} has no Branch field and heal gave none`);
  return { ...part, branch };
}

export function postSkipOnce({ number, reason, comments, post }) {
  return postOnce({
    comments,
    marker: skipMarker(number),
    body: `Autobuild pominął #${number}: ${reason}. Karta zostaje w obecnym statusie, a kolejny przebieg oceni ją ponownie.`,
    post,
  });
}

function skipReason(card, item, blocked) {
  const problem = tierProblem(card);
  if (problem) return problem;
  const dependency = [...(item.dependsOn ?? []), ...(card.dependsOn ?? [])].find((d) => blocked.has(d));
  return dependency === undefined ? null : `zależy od #${dependency}`;
}

export async function runQueue({ items = Infinity, includeManual = false, only = null, readCards, buildPart, nextPart = null, isClosed = async () => false, finishEpic = async () => {}, onSkip = async () => {}, log = () => {} }) {
  const started = new Set();
  const finished = new Set();
  const closedParts = new Set();
  const skipped = [];
  const blocked = new Set();
  let previous = null;

  const skip = async (number, reason) => {
    skipped.push({ number, reason });
    blocked.add(number);
    log(`pominięto #${number}: ${reason}`);
    await onSkip({ number, reason });
  };

  for (;;) {
    // the project's "Item closed" workflow moves a merged part's card to Done asynchronously, so a closed issue is Done here already
    const cards = (await readCards()).map((c) => (closedParts.has(c.number) ? { ...c, status: "Done" } : c));
    const queue = [];
    for (const q of queueOf(cards)) {
      if (finished.has(q.number)) continue;
      queue.push(nextPart && q.epic && started.has(q.number) ? { ...q, next: await nextPart(q) } : q);
    }

    const closable = queue.find((q) => q.epic && started.has(q.number) && q.next === null);
    if (closable) {
      await finishEpic(closable, cards);
      finished.add(closable.number);
      continue;
    }

    const item = only !== null && only !== undefined ? planOne(queue, Number(only), cards)[0] : pickNext(queue, { started, items, includeManual });
    if (!item) return { skipped };

    const part = nextPart && item.epic ? (started.has(item.number) ? item.next : await nextPart(item)) : nextWork(item, cards);
    const partCard = part === null ? null : cards.find((c) => c.number === part);
    const reason = partCard ? skipReason(partCard, item, blocked) : null;
    if (reason) {
      await skip(part, reason);
      if (item.epic) {
        const numbers = partNumbers(item);
        for (const n of numbers.slice(numbers.indexOf(part) + 1)) {
          const later = cards.find((c) => c.number === n);
          if (later && later.status !== "Done") await skip(n, `zależy od #${part}`);
        }
      }
      blocked.add(item.number);
      finished.add(item.number);
      continue;
    }

    const continuation = queue.find((q) => q.number === previous && q.epic && q.next !== null);
    if (continuation && continuation.number !== item.number) log(`kolejka zmieniona: #${item.number} (${shortOf(item.priority)}) przed #${continuation.number}`);

    started.add(item.number);
    if (part === null) {
      await finishEpic(item, cards);
      finished.add(item.number);
      continue;
    }

    const result = await buildPart(item, part, cards);
    if (!item.epic || result === false) finished.add(item.number);
    else if (await isClosed(part)) closedParts.add(part);
    previous = item.number;
  }
}

export function areaWarning(item, cards) {
  const number = nextWork(item, cards);
  if (number === null) return null;
  const card = cards.find((c) => c.number === number);
  return card && !card.area ? `⚠ #${number} bez obszaru (Area)` : null;
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
