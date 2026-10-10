import assert from "node:assert/strict";
import { mkdirSync, writeFileSync } from "node:fs";
import path from "node:path";
import { after, before, test } from "node:test";
import { endpointsOf, makeSandbox, readFacts, runFacts, serviceOf } from "./helpers.mjs";

let sb;
let facts;

before(() => {
  sb = makeSandbox();
  const leftover = path.join(sb.root, "services", "Strategy", "Skarbiec.Strategy", "bin", "Debug");
  mkdirSync(leftover, { recursive: true });
  writeFileSync(path.join(leftover, "Skarbiec.Strategy.dll"), "x");
  const run = runFacts(sb);
  assert.equal(run.status, 0, run.output);
  facts = readFacts(sb);
});

after(() => sb.cleanup());

test("facts lists the services that have a Program.cs and skips leftover folders", () => {
  const names = facts.pages.architektura.facts.services.map((s) => s.name).sort();
  assert.deepEqual(names, ["Identity", "MarketData", "Portfolio", "Reporting"]);
});

test("facts lists every slice folder holding an Endpoint file, nested folders by their leaf name", () => {
  const slices = (name) => serviceOf(facts, name).slices.map((s) => s.name).sort();
  assert.deepEqual(slices("Identity"), ["Login"]);
  assert.deepEqual(slices("Portfolio"), ["AddAsset", "GetBond"]);
  assert.deepEqual(slices("MarketData"), ["GetInstrument", "GetInstrumentsBatch", "GetLatestPrices"]);
  assert.deepEqual(slices("Reporting"), ["GetSnapshot"]);
  const bond = serviceOf(facts, "Portfolio").slices.find((s) => s.name === "GetBond");
  assert.equal(bond.folder, "Bonds/GetBond");
});

test("facts joins MapGroup and inner Map literals into the full path with its verb", () => {
  assert.deepEqual(endpointsOf(serviceOf(facts, "Identity")), ["GET /api/identity/me", "POST /api/identity/login/token"].sort());
  assert.deepEqual(endpointsOf(serviceOf(facts, "Portfolio")), [
    "GET /api/portfolio/bonds/{id:guid}",
    "GET /api/portfolio/ping",
    "POST /api/portfolio/assets/{portfolioId:guid}",
  ].sort());
  assert.deepEqual(endpointsOf(serviceOf(facts, "Reporting")), ["GET /api/reporting/snapshots/latest"]);
});

test("facts maps MapInternalGroup endpoints under /internal, chained or through a variable", () => {
  assert.deepEqual(endpointsOf(serviceOf(facts, "MarketData")), [
    "GET /api/marketdata/instruments/{id:guid}",
    "GET /internal/instruments/{id:guid}",
    "POST /internal/instruments/batch",
    "POST /internal/prices/latest-batch",
  ].sort());
});

test("facts keeps a public and an internal route mapped in one file on the same slice", () => {
  const slice = serviceOf(facts, "MarketData").slices.find((s) => s.name === "GetInstrument");
  const routes = slice.endpoints.map((e) => `${e.verb} ${e.path}`).sort();
  assert.deepEqual(routes, ["GET /api/marketdata/instruments/{id:guid}", "GET /internal/instruments/{id:guid}"]);
});

test("facts lists inline Program.cs endpoints outside any slice", () => {
  assert.deepEqual(serviceOf(facts, "Identity").inlineEndpoints, [{ verb: "GET", path: "/api/identity/me" }]);
  assert.deepEqual(serviceOf(facts, "Portfolio").inlineEndpoints, [{ verb: "GET", path: "/api/portfolio/ping" }]);
});
