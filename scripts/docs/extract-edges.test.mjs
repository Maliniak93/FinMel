import assert from "node:assert/strict";
import { after, before, test } from "node:test";
import { byJson, makeSandbox, readFacts, runFacts } from "./helpers.mjs";

let sb;
let arch;

before(() => {
  sb = makeSandbox();
  const run = runFacts(sb);
  assert.equal(run.status, 0, run.output);
  arch = readFacts(sb).pages.architektura.facts;
});

after(() => sb.cleanup());

test("REST edges link the caller to the callee endpoint and flag the unmatched path", () => {
  assert.deepEqual(byJson(arch.restEdges), byJson([
    { from: "Portfolio", to: "MarketData", path: "/internal/instruments/batch", unmatched: false },
    { from: "Reporting", to: null, path: "/internal/prices/ghost-batch", unmatched: true },
  ]));
});

test("event edges link each publisher service to every consumer service, over direct, multi-line and wrapper publishes", () => {
  assert.deepEqual(byJson(arch.eventEdges), byJson([
    { event: "AssetPositionChanged", from: "Portfolio", to: "MarketData" },
    { event: "AssetPositionChanged", from: "Portfolio", to: "Reporting" },
    { event: "DailyPricesSynced", from: "MarketData", to: "Reporting" },
    { event: "PortfolioArchived", from: "Portfolio", to: "Reporting" },
    { event: "PortfolioHistoryRebuildRequested", from: "Reporting", to: "Reporting" },
  ]));
});

test("gateway routes carry id, path, cluster and the authorization and rate-limit policies", () => {
  assert.deepEqual(byJson(arch.gatewayRoutes), byJson([
    {
      id: "identity-login",
      path: "/api/identity/login/{**catch-all}",
      cluster: "identity-cluster",
      authorizationPolicy: "anonymous",
      rateLimiterPolicy: "auth",
    },
    {
      id: "portfolio-protected",
      path: "/api/portfolio/{**catch-all}",
      cluster: "portfolio-cluster",
      authorizationPolicy: "default",
      rateLimiterPolicy: "standard",
    },
  ]));
});
