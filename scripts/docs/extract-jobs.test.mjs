import assert from "node:assert/strict";
import { after, before, test } from "node:test";
import { hasToken, makeSandbox, mentions, pageOf, readFacts, runFacts, section } from "./helpers.mjs";

let sb;
let page;

before(() => {
  sb = makeSandbox();
  const run = runFacts(sb);
  assert.equal(run.status, 0, run.output);
  page = pageOf(readFacts(sb), "joby-integracje-frontend");
});

after(() => sb.cleanup());

test("a scheduled job lists its key, its class, the appsettings.json cron as effective and the Development cron next to it", () => {
  const priceSync = section(page, "jobs").find((j) => hasToken(j, "PriceSyncJob"));
  assert.ok(priceSync, "PriceSyncJob missing from the jobs");
  assert.ok(mentions(priceSync, "price-sync"), "job key");
  assert.ok(mentions(priceSync, "0 0 19 ? * MON-FRI"), "effective cron from appsettings.json");
  assert.ok(mentions(priceSync, "0 */2 * * * ?"), "Development cron");
});

test("a job with no cron trigger is listed as na żądanie", () => {
  const backfill = section(page, "jobs").find((j) => hasToken(j, "HistoryBackfillJob"));
  assert.ok(backfill, "HistoryBackfillJob missing from the jobs");
  assert.ok(mentions(backfill, "na żądanie"));
});

test("a startup trigger is listed with the interface or base type it implements", () => {
  const startup = section(page, "triggers").find((t) => hasToken(t, "BondCatalogStartupTrigger"));
  assert.ok(startup, "BondCatalogStartupTrigger missing from the triggers");
  assert.ok(mentions(startup, "BackgroundService"));
});
