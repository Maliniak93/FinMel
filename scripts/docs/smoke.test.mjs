import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { REPO_ROOT, runDocs } from "./helpers.mjs";

test("facts on this repository finds every service, an internal edge, an event edge and a gateway route", () => {
  const out = mkdtempSync(path.join(os.tmpdir(), "docs-smoke-"));
  try {
    const run = runDocs(["facts", "--root", REPO_ROOT, "--out", out]);
    assert.equal(run.status, 0, run.output);

    const arch = JSON.parse(readFileSync(path.join(out, ".work", "facts.json"), "utf8")).pages.architektura.facts;
    const names = arch.services.map((s) => s.name);
    assert.ok(!names.includes("Strategy"));
    for (const name of ["Identity", "Portfolio", "MarketData", "Reporting"]) {
      const service = arch.services.find((s) => s.name === name);
      assert.ok(service, `${name} missing`);
      assert.ok(service.slices.length > 0, `${name} has no slices`);
      const endpoints = service.slices.flatMap((s) => s.endpoints).length + service.inlineEndpoints.length;
      assert.ok(endpoints > 0, `${name} has no endpoints`);
    }
    assert.ok(arch.restEdges.length > 0);
    assert.ok(arch.eventEdges.length > 0);
    assert.ok(arch.gatewayRoutes.length > 0);
  } finally {
    rmSync(out, { recursive: true, force: true });
  }
});

test("facts on this repository lists the contract events, the internal message, ten consumers, an ERD per service and RefreshToken without a tenancy filter", () => {
  const out = mkdtempSync(path.join(os.tmpdir(), "docs-smoke-"));
  try {
    const run = runDocs(["facts", "--root", REPO_ROOT, "--out", out]);
    assert.equal(run.status, 0, run.output);

    const pages = JSON.parse(readFileSync(path.join(out, ".work", "facts.json"), "utf8")).pages;
    const messages = pages.eventy.facts.messages;
    assert.deepEqual(
      messages.filter((m) => m.kind === "event").map((m) => m.name).sort(),
      [
        "AssetPositionChanged",
        "AssetRemoved",
        "DailyPricesSynced",
        "InstrumentHistoryBackfilled",
        "PortfolioArchived",
        "PortfolioDeleted",
        "PortfolioRestored",
        "UserRegistered",
      ],
    );
    assert.equal(messages.find((m) => m.name === "PortfolioHistoryRebuildRequested")?.kind, "internal");
    assert.equal(messages.flatMap((m) => m.consumers).length, 10);

    const services = pages["bazy-danych"].facts.services;
    assert.deepEqual(services.map((s) => s.name).sort(), ["Identity", "MarketData", "Portfolio", "Reporting"]);
    for (const service of services) assert.ok(service.entities.length > 0, `${service.name} has no entities`);
    const refreshTokens = services.find((s) => s.name === "Identity").entities.find((e) => e.table === "RefreshTokens");
    assert.equal(refreshTokens?.tenancy, "UserId bez filtra");
  } finally {
    rmSync(out, { recursive: true, force: true });
  }
});
