import assert from "node:assert/strict";
import { test } from "node:test";
import { branchFor, slugify } from "./branch-name.mjs";

test("slugify transliterates Polish letters into a kebab slug", () => {
  assert.equal(slugify("Pasek boczny z grupami", 1), "pasek-boczny-z-grupami");
  assert.equal(slugify("Zażółć gęślą jaźń: ĄĆĘŁŃÓŚŹŻ", 1), "zazolc-gesla-jazn-acelnoszz");
});

test("slugify collapses special characters and trims", () => {
  assert.equal(slugify("  Web: dashboard -- wg grup (#508)!  ", 1), "web-dashboard-wg-grup-508");
});

test("slugify falls back to issue-<n> for an empty result", () => {
  assert.equal(slugify("!!! ???", 466), "issue-466");
  assert.equal(slugify("", 7), "issue-7");
});

test("slugify cuts at a word edge within 50 characters", () => {
  const slug = slugify("Karta bez wypełnionych pól nie zatrzymuje flow autobuilda w nocy", 1);
  assert.equal(slug, "karta-bez-wypelnionych-pol-nie-zatrzymuje-flow");
  assert.ok(slug.length <= 50);
  assert.equal(slugify("a".repeat(80), 1), "a".repeat(50));
});

test("branchFor prefixes fix/ for Kind Fix and feat/ otherwise", () => {
  assert.equal(branchFor("Fix", "x"), "fix/x");
  assert.equal(branchFor("fix", "x"), "fix/x");
  for (const kind of ["New", "Change", "Cleanup", "change", null]) assert.equal(branchFor(kind, "x"), "feat/x");
});
