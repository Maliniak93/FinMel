import assert from "node:assert/strict";
import { after, before, test } from "node:test";
import { byJson, makeSandbox, readFacts, runFacts } from "./helpers.mjs";

let sb;
let messages;

before(() => {
  sb = makeSandbox();
  const run = runFacts(sb);
  assert.equal(run.status, 0, run.output);
  messages = readFacts(sb).pages.eventy.facts.messages;
});

after(() => sb.cleanup());

const byName = (name) => messages.find((m) => m.name === name);
const normalize = (m) => ({ ...m, publishers: byJson(m.publishers), consumers: byJson(m.consumers) });

test("each message lists its properties, publishers and production consumers, with the nested DTO expanded one level", () => {
  assert.deepEqual(
    messages.map((m) => m.name),
    ["AssetPositionChanged", "DailyPricesSynced", "PortfolioArchived", "PortfolioHistoryRebuildRequested", "UserRegistered"],
  );
  assert.deepEqual(normalize(byName("AssetPositionChanged")), normalize({
    name: "AssetPositionChanged",
    kind: "event",
    properties: [
      { name: "AssetId", type: "Guid" },
      { name: "PortfolioId", type: "Guid" },
      { name: "UserId", type: "Guid" },
      { name: "Currency", type: "string" },
      { name: "Quantity", type: "decimal" },
      {
        name: "QuantityHistory",
        type: "IReadOnlyList<QuantityPoint>",
        nested: [
          { name: "Date", type: "DateOnly" },
          { name: "Quantity", type: "decimal" },
        ],
      },
    ],
    publishers: [{ service: "Portfolio", file: "Features/AddAsset/AddAssetHandler.cs" }],
    consumers: [
      { service: "MarketData", consumer: "AssetPositionChangedConsumer", definition: "AssetPositionChangedConsumerDefinition" },
      { service: "Reporting", consumer: "AssetPositionChangedConsumer", definition: null },
    ],
  }));
  assert.deepEqual(normalize(byName("DailyPricesSynced")), normalize({
    name: "DailyPricesSynced",
    kind: "event",
    properties: [
      { name: "Date", type: "DateOnly" },
      { name: "Kind", type: "PriceSyncKind" },
    ],
    publishers: [{ service: "MarketData", file: "Jobs/PriceSyncJob.cs" }],
    consumers: [{ service: "Reporting", consumer: "DailyPricesSyncedConsumer", definition: null }],
  }));
  assert.deepEqual(normalize(byName("PortfolioArchived")), normalize({
    name: "PortfolioArchived",
    kind: "event",
    properties: [{ name: "PortfolioId", type: "Guid" }],
    publishers: [{ service: "Portfolio", file: "Features/PositionEventPublisher.cs" }],
    consumers: [{ service: "Reporting", consumer: "PortfolioArchivedConsumer", definition: null }],
  }));
  assert.deepEqual(normalize(byName("PortfolioHistoryRebuildRequested")), normalize({
    name: "PortfolioHistoryRebuildRequested",
    kind: "internal",
    properties: [
      { name: "PortfolioId", type: "Guid" },
      { name: "UserId", type: "Guid" },
    ],
    publishers: [{ service: "Reporting", file: "Messaging/HistoryRebuildRequests.cs" }],
    consumers: [{ service: "Reporting", consumer: "PortfolioHistoryRebuildConsumer", definition: null }],
  }));
});

test("an event with no production consumer keeps an empty consumer list beside a consumer in a Tests project", () => {
  assert.deepEqual(byName("UserRegistered").consumers, []);
  assert.deepEqual(byName("UserRegistered").publishers, [{ service: "Identity", file: "Features/Register/RegisterUserHandler.cs" }]);
});
