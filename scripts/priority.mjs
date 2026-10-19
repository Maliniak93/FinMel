// Usage: imported by queue.mjs and gh-project.mjs - priority levels, their bands on the board and where an item lands in its band

export const PRIORITIES = ["P0 Pilne", "P1 Wysoki", "P2 Normalny", "P3 Niski"];
export const DEFAULT_PRIORITY = PRIORITIES[2];

const itemIdOf = (card) => card.id ?? card.itemId ?? null;

export function parsePriority(value) {
  const match = /^p?([0-3])(?:\s.*)?$/i.exec(String(value ?? "").trim());
  return match ? PRIORITIES[Number(match[1])] : null;
}

export const shortOf = (priority) => (priority ?? DEFAULT_PRIORITY).slice(0, 2);

export function rankOf(card) {
  const at = PRIORITIES.indexOf(card.priority);
  return at < 0 ? PRIORITIES.indexOf(DEFAULT_PRIORITY) : at;
}

export function placeTarget(queue, board, { priority, spec = "bottom", self = null }) {
  const rank = PRIORITIES.indexOf(priority);
  if (rank < 0) throw new Error(`unknown priority "${priority}"`);
  const members = new Set(queue.map((q) => q.number));
  const todo = board.filter((b) => members.has(b.number) && b.status === "Todo" && b.number !== self);
  const lastAt = (keep) => {
    const found = todo.filter(keep);
    return found.length ? itemIdOf(found[found.length - 1]) : null;
  };

  if (spec === "bottom") return lastAt((c) => rankOf(c) <= rank);
  if (spec === "top") return lastAt((c) => rankOf(c) < rank);

  const match = /^(before|after):(\d+)$/.exec(spec);
  if (!match) throw new Error(`unknown queue position "${spec}": use top, bottom, before:N or after:N`);

  const at = todo.findIndex((c) => c.number === Number(match[2]));
  if (at < 0) throw new Error(`#${match[2]} is not a Todo item in the queue`);
  const target = todo[at];
  if (rankOf(target) !== rank) {
    const band = shortOf(PRIORITIES[rankOf(target)]);
    const mine = shortOf(priority);
    throw new Error(
      self === null
        ? `#${target.number} jest w paśmie ${band}, a nowy element w ${mine} — ustaw --priority ${band} albo użyj /prio po utworzeniu`
        : `#${target.number} jest w paśmie ${band}, a #${self} w ${mine} — zmień priorytet: /prio #${self} ${band}`,
    );
  }
  if (match[1] === "after") return itemIdOf(target);
  return at === 0 ? null : itemIdOf(todo[at - 1]);
}

export function inBand(board, card) {
  const todo = board.filter((b) => b.status === "Todo");
  const at = todo.findIndex((b) => b.number === card.number);
  const rank = rankOf(card);
  return todo.every((b, i) => i === at || (i < at ? rankOf(b) <= rank : rankOf(b) >= rank));
}

export function describeSpot(queue, number) {
  const at = queue.findIndex((q) => q.number === number);
  if (at <= 0) return "1. w kolejce (góra Todo)";
  const before = queue[at - 1];
  return `${at + 1}. w kolejce, za #${before.number} (${shortOf(before.priority)})`;
}
