import assert from "node:assert/strict";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { hasToken, mentions, pageOf, REPO_ROOT, runDocs, section } from "./helpers.mjs";

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

test("facts on this repository lists the three scheduled jobs with their cron, a caller for each external API, the dashboard route mapped and ADR-025 and ADR-032 as weak points", () => {
  const out = mkdtempSync(path.join(os.tmpdir(), "docs-smoke-"));
  try {
    const run = runDocs(["facts", "--root", REPO_ROOT, "--out", out]);
    assert.equal(run.status, 0, run.output);

    const facts = JSON.parse(readFileSync(path.join(out, ".work", "facts.json"), "utf8"));
    const joby = pageOf(facts, "joby-integracje-frontend");
    const jobs = section(joby, "jobs");
    for (const [key, cron] of [
      ["price-sync", "0 30 18 ? * MON-FRI"],
      ["fx-sync", "0 0 13 ? * MON-FRI"],
      ["bond-catalog-sync", "0 0 7 * * ?"],
    ]) {
      const job = jobs.find((j) => mentions(j, key));
      assert.ok(job, `${key} missing from the jobs`);
      assert.ok(mentions(job, cron), `${key} cron ${cron}`);
    }

    assert.ok(jobs.find((j) => hasToken(j, "HistoryBackfillJob")), "HistoryBackfillJob missing from the jobs");

    const integrations = section(joby, "integrations");
    for (const [client, caller] of [
      ["YahooApiClient", "PriceSyncJob"],
      ["NbpApiClient", "FxSyncJob"],
      ["GoldApiClient", "PriceSyncJob"],
      ["MfBondSource", "BondCatalogSyncJob"],
    ]) {
      const integration = integrations.find((i) => mentions(i, client));
      assert.ok(integration, `${client} missing from the integrations`);
      assert.ok(hasToken(integration, caller), `${client} has no caller ${caller}`);
    }

    const dashboard = section(joby, "routes").find((r) => mentions(r, "features/dashboard/dashboard"));
    assert.ok(dashboard, "dashboard route missing");
    assert.ok(mentions(dashboard, "/api/reporting/dashboard"));

    const items = section(pageOf(facts, "slabe-punkty"), "items");
    for (const adr of ["ADR-025", "ADR-032"]) {
      assert.ok(items.some((i) => mentions(i, adr)), `${adr} missing from the weak points`);
    }
  } finally {
    rmSync(out, { recursive: true, force: true });
  }
});
