import assert from "node:assert/strict";
import { after, before, test } from "node:test";
import { hasToken, inOrderTokens, makeSandbox, mentions, pageOf, readFacts, runFacts, section } from "./helpers.mjs";

let sb;
let integrations;

before(() => {
  sb = makeSandbox();
  const run = runFacts(sb);
  assert.equal(run.status, 0, run.output);
  integrations = section(pageOf(readFacts(sb), "joby-integracje-frontend"), "integrations");
});

after(() => sb.cleanup());

const integration = (client) => {
  const found = integrations.find((i) => hasToken(i, client));
  assert.ok(found, `${client} missing from the integrations`);
  return found;
};

test("a literal base URL and an Options default overridden in appsettings.json both show as the effective base URL", () => {
  assert.ok(mentions(integration("YahooApiClient"), "https://yahoo.fixture.test/"));

  const stooq = integration("StooqApiClient");
  assert.ok(mentions(stooq, "https://stooq.fixture.test/"), "appsettings.json override");
  assert.ok(!mentions(stooq, "api.stooq.example"), "the Options default is overridden, not shown");
});

test("each client shows its caller chain client, then source class, then job, with IEnumerable<IPriceSource> resolved", () => {
  assert.ok(inOrderTokens(integration("YahooApiClient"), ["YahooApiClient", "YahooPriceSource", "PriceSyncJob"]));
  assert.ok(inOrderTokens(integration("StooqApiClient"), ["StooqApiClient", "StooqApiPriceSource", "PriceSyncJob"]));
});
