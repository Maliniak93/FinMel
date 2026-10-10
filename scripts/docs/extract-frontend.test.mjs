import assert from "node:assert/strict";
import { after, before, test } from "node:test";
import { makeSandbox, mentions, pageOf, readFacts, runFacts, section, strings } from "./helpers.mjs";

let sb;
let routes;

before(() => {
  sb = makeSandbox();
  const run = runFacts(sb);
  assert.equal(run.status, 0, run.output);
  routes = section(pageOf(readFacts(sb), "joby-integracje-frontend"), "routes");
});

after(() => sb.cleanup());

const dashboard = () => {
  const route = routes.find((r) => mentions(r, "features/dashboard/dashboard"));
  assert.ok(route, "dashboard route missing");
  return route;
};

test("the dashboard child route maps its component's calls, from the component and from a nested folder, to their method, path and backend endpoints", () => {
  const route = dashboard();
  assert.ok(mentions(route, "bonds-card"), "nested folder file included");
  assert.ok(mentions(route, "GET"));
  assert.ok(mentions(route, "/api/reporting/snapshots/latest"));
  assert.ok(mentions(route, "/api/portfolio/bonds/{id}"));
  assert.ok(mentions(route, "/api/portfolio/bonds/{id:guid}"), "backend endpoint route");
  const anchors = new Set(strings(route).filter((s) => /architektura\.html#/.test(s)));
  assert.ok(anchors.size >= 2, "one Architektura anchor per backend endpoint");
});

test("a type-only import of a client function is not a call and adds no method and path to the dashboard route", () => {
  assert.ok(!mentions(dashboard(), "/api/portfolio/me"));
});

test("the redirect route is listed as a redirect to dashboard", () => {
  const redirect = routes.find(
    (r) => /redirect/i.test(JSON.stringify(r)) && strings(r).some((s) => s.replace(/^\//, "") === "dashboard"),
  );
  assert.ok(redirect, "redirect route missing");
});
