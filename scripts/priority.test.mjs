import assert from "node:assert/strict";
import test from "node:test";
import { describeSpot, parsePriority, placeTarget } from "./priority.mjs";
import { queueOf } from "./queue.mjs";

const card = (number, status, priority = null) => ({
  number,
  title: `Issue ${number}`,
  id: `ITEM_${number}`,
  itemId: `ITEM_${number}`,
  status,
  tier: "1",
  kind: "Change",
  branch: null,
  epic: false,
  skipTests: false,
  e2e: false,
  autobuild: false,
  parent: null,
  priority,
});

const afterOf = (value) => (value && typeof value === "object" && "afterId" in value ? value.afterId : value);

function place(board, priority, spec = "bottom", self = 999) {
  const queue = queueOf(board);
  return afterOf(placeTarget(queue, board, { priority, spec, self }));
}

test("placement follows bands", () => {
  const board = [card(440, "Todo", "P1 Wysoki"), card(441, "Todo", "P2 Normalny"), card(442, "Todo", "P2 Normalny"), card(443, "Todo", null)];

  assert.equal(place(board, "P1 Wysoki"), "ITEM_440");
  assert.equal(place(board, "P0 Pilne"), null);
  assert.equal(place(board, "P3 Niski"), "ITEM_443");
  assert.equal(place(board, "P2 Normalny"), "ITEM_443");
});

test("parsePriority", () => {
  assert.equal(parsePriority("p1"), "P1 Wysoki");
  assert.equal(parsePriority("1"), "P1 Wysoki");
  assert.equal(parsePriority("P1 Wysoki"), "P1 Wysoki");
  assert.equal(parsePriority("P5"), null);
});

test("describeSpot", () => {
  const board = [card(440, "Todo", "P1 Wysoki"), card(441, "Todo", "P2 Normalny"), card(442, "Todo", "P2 Normalny"), card(443, "Todo", null)];
  const afterId = place(board, "P1 Wysoki");
  assert.equal(afterId, "ITEM_440");

  const withNew = [board[0], card(444, "Todo", "P1 Wysoki"), ...board.slice(1)];
  assert.equal(describeSpot(queueOf(withNew), 444), "2. w kolejce, za #440 (P1)");

  const atTop = [card(445, "Todo", "P0 Pilne"), ...board];
  assert.equal(describeSpot(queueOf(atTop), 445), "1. w kolejce (góra Todo)");
});
