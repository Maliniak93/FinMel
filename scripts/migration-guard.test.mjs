import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { checkChange, consentProblems, findDestructive, historyViolations, parseNameStatus, testExists } from "./migration-guard.mjs";

const REPO_ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

const migration = (up, down = "") => `using Microsoft.EntityFrameworkCore.Migrations;

public partial class Sample : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
${up}
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
${down}
    }
}
`;

const keys = (source, service = "Portfolio") => findDestructive(source, service).map((f) => f.key);

const REMOVE_MODEL = migration(
  `        migrationBuilder.DropTable(
            name: "AllocationBalanceOverrides");

        migrationBuilder.DropColumn(
            name: "MinimumTransferPln",
            table: "PlanningSettings");`,
  `        migrationBuilder.AddColumn<int>(name: "MinimumTransferPln", table: "PlanningSettings", type: "integer", nullable: false, defaultValue: 0);`,
);

test("finds destructive operations in Up only", () => {
  assert.deepEqual(keys(REMOVE_MODEL), ["Portfolio.AllocationBalanceOverrides", "Portfolio.PlanningSettings.MinimumTransferPln"]);

  const downOnly = migration(
    `        migrationBuilder.AddColumn<int>(name: "Extra", table: "PlanningSettings", type: "integer", nullable: false, defaultValue: 0);`,
    `        migrationBuilder.DropTable(
            name: "Accounts");

        migrationBuilder.DropColumn(name: "Extra", table: "PlanningSettings");`,
  );
  assert.deepEqual(keys(downOnly), []);

  const sql = migration(`        migrationBuilder.Sql("DELETE FROM \\"Transactions\\" WHERE \\"Quantity\\" = 0;");
        migrationBuilder.Sql(@"TRUNCATE TABLE ""Assets"";");
        migrationBuilder.Sql("ALTER TABLE \\"Accounts\\" DROP COLUMN \\"Note\\";");
        migrationBuilder.Sql("UPDATE \\"Accounts\\" SET \\"Name\\" = 'x';");
        migrationBuilder.Sql("DROP INDEX \\"IX_Accounts_Name\\";");`);
  assert.deepEqual(keys(sql), ["Portfolio.Transactions", "Portfolio.Assets", "Portfolio.Accounts.Note"]);

  const harmless = migration(`        migrationBuilder.RenameColumn(name: "A", table: "Accounts", newName: "B");
        migrationBuilder.RenameTable(name: "Old", newName: "New");
        migrationBuilder.CreateTable(name: "Fresh", columns: table => new { Id = table.Column<Guid>(nullable: false) }, constraints: table => { table.PrimaryKey("PK_Fresh", x => x.Id); });
        migrationBuilder.DropIndex(name: "IX_A", table: "Accounts");
        migrationBuilder.AddColumn<int>(name: "C", table: "Accounts", nullable: false, defaultValue: 0);`);
  assert.deepEqual(keys(harmless), []);

  const withStringBraces = migration(`        migrationBuilder.Sql("SELECT '{'; DELETE FROM \\"Assets\\";");
        migrationBuilder.DropTable(name: "Accounts");`);
  assert.deepEqual(keys(withStringBraces), ["Portfolio.Assets", "Portfolio.Accounts"]);
});

test("alter column narrowing", () => {
  const alter = (generic, args) => migration(`        migrationBuilder.AlterColumn<${generic}>(
            name: "Col",
            table: "Accounts",
            ${args});`);

  assert.deepEqual(keys(alter("string", `type: "character varying(10)", maxLength: 10, nullable: false, oldClrType: typeof(string), oldType: "character varying(20)", oldMaxLength: 20`)), ["Portfolio.Accounts.Col"]);
  assert.deepEqual(keys(alter("string", `type: "text", nullable: false, oldClrType: typeof(int), oldType: "integer"`)), ["Portfolio.Accounts.Col"]);
  assert.deepEqual(keys(alter("decimal", `type: "numeric(10,2)", precision: 10, scale: 2, nullable: false, oldClrType: typeof(decimal), oldType: "numeric(18,2)", oldPrecision: 18, oldScale: 2`)), ["Portfolio.Accounts.Col"]);
  assert.deepEqual(keys(alter("string", `type: "character varying(20)", maxLength: 20, nullable: false, oldClrType: typeof(string), oldType: "text"`)), ["Portfolio.Accounts.Col"]);

  assert.deepEqual(keys(alter("string", `type: "character varying(50)", maxLength: 50, nullable: false, oldClrType: typeof(string), oldType: "character varying(20)", oldMaxLength: 20`)), []);
  assert.deepEqual(keys(alter("string", `type: "text", nullable: false, oldClrType: typeof(string), oldType: "character varying(20)", oldMaxLength: 20`)), []);
  assert.deepEqual(keys(alter("decimal", `type: "numeric(18,4)", precision: 18, scale: 4, nullable: false, oldClrType: typeof(decimal), oldType: "numeric(18,2)", oldPrecision: 18, oldScale: 2`)), []);
  assert.deepEqual(keys(alter("string", `type: "character varying(20)", maxLength: 20, nullable: true, oldClrType: typeof(string), oldType: "character varying(20)", oldMaxLength: 20`)), []);
  assert.deepEqual(keys(alter("int", `type: "integer", nullable: true, oldClrType: typeof(int), oldType: "integer"`)), []);
});

test("derived tables are free", () => {
  const drops = (...tables) => migration(tables.map((t) => `        migrationBuilder.DropTable(name: "${t}");`).join("\n"));

  assert.deepEqual(keys(drops("ValuationReplicas", "PortfolioOutboxMessage", "PortfolioOutboxState", "PortfolioInboxState")), []);
  assert.deepEqual(keys(drops("ValuationReplicas", "Accounts")), ["Portfolio.Accounts"]);
  assert.deepEqual(keys(drops("Positions", "AssetValuations", "ReportingOutboxMessage", "SomethingNew"), "Reporting"), []);
  assert.deepEqual(keys(drops("Instruments", "PriceQuotes", "FxRates"), "MarketData"), ["MarketData.Instruments"]);
  assert.deepEqual(keys(drops("AspNetUsers", "RefreshTokens"), "Identity"), ["Identity.AspNetUsers", "Identity.RefreshTokens"]);
});

test("history only grows", () => {
  const dir = "services/Portfolio/Skarbiec.Portfolio/Migrations";
  const entries = parseNameStatus(
    [
      `M\t${dir}/20261022140947_RemoveOldAllocationModel.cs`,
      `D\t${dir}/20261022140947_RemoveOldAllocationModel.Designer.cs`,
      `R100\t${dir}/20261020161926_A.cs\t${dir}/20261020161926_B.cs`,
      `M\t${dir}/PortfolioDbContextModelSnapshot.cs`,
      `A\t${dir}/20270101000000_New.cs`,
      `A\t${dir}/20270101000000_New.Designer.cs`,
      "M\tscripts/verify.mjs",
    ].join("\n"),
  );

  assert.deepEqual(
    historyViolations(entries).map((e) => e.path),
    [`${dir}/20261022140947_RemoveOldAllocationModel.cs`, `${dir}/20261022140947_RemoveOldAllocationModel.Designer.cs`, `${dir}/20261020161926_B.cs`],
  );
  assert.deepEqual(historyViolations(entries.filter((e) => /ModelSnapshot|New|verify/.test(e.path))), []);

  const result = checkChange({ files: entries.slice(0, 1), body: "## Utrata danych\n\n- Portfolio.Anything\n\nZgoda: tak\n" });
  assert.equal(result.ok, false);
  assert.equal(result.history.length, 1);
});

test("consent sections", () => {
  const dir = "services/Portfolio/Skarbiec.Portfolio/Migrations";
  const files = [{ status: "A", path: `${dir}/20270101000000_RemoveOldAllocationModel.cs` }];
  const readSource = () => REMOVE_MODEL;
  const proofs = new Set(["MoveTests.CopiesData"]);
  const check = (body) => checkChange({ files, body, readSource, proofExists: (p) => proofs.has(p) });

  const none = check("## Goal\n\nx\n");
  assert.equal(none.ok, false);
  assert.deepEqual(none.missing.map((m) => m.key), ["Portfolio.AllocationBalanceOverrides", "Portfolio.PlanningSettings.MinimumTransferPln"]);

  const noZgoda = check("## Utrata danych\n\n- Portfolio.AllocationBalanceOverrides\n- Portfolio.PlanningSettings.MinimumTransferPln\n");
  assert.equal(noZgoda.ok, false);
  assert.equal(noZgoda.missing.length, 2);
  assert.match(noZgoda.missing[0].reason, /Zgoda/);

  const partial = check("## Utrata danych\n\n- Portfolio.AllocationBalanceOverrides\n\nZgoda: tak, dane testowe\n");
  assert.deepEqual(partial.missing.map((m) => m.key), ["Portfolio.PlanningSettings.MinimumTransferPln"]);

  const full = check("## Utrata danych\n\n- `Portfolio.AllocationBalanceOverrides` - stary model\n- Portfolio.PlanningSettings.MinimumTransferPln\n\nZgoda: tak, dane testowe\n\n## Out of scope\n");
  assert.equal(full.ok, true, JSON.stringify(full.missing));

  const moved = check(
    "## Przeniesienie danych\n\n- Portfolio.AllocationBalanceOverrides - proof: MoveTests.CopiesData\n- Portfolio.PlanningSettings.MinimumTransferPln - proof: `MoveTests.CopiesData`\n",
  );
  assert.equal(moved.ok, true, JSON.stringify(moved.missing));

  const ghostProof = check("## Przeniesienie danych\n\n- Portfolio.AllocationBalanceOverrides - proof: GhostTests.Nothing\n- Portfolio.PlanningSettings.MinimumTransferPln - proof: MoveTests.CopiesData\n");
  assert.deepEqual(ghostProof.missing.map((m) => m.key), ["Portfolio.AllocationBalanceOverrides"]);
  assert.match(ghostProof.missing[0].reason, /GhostTests\.Nothing/);

  const mixed = check("## Utrata danych\n\n- Portfolio.AllocationBalanceOverrides\nZgoda: tak\n\n## Przeniesienie danych\n\n- Portfolio.PlanningSettings.MinimumTransferPln proof: MoveTests.CopiesData\n");
  assert.equal(mixed.ok, true, JSON.stringify(mixed.missing));

  assert.equal(checkChange({ files: [{ status: "A", path: `${dir}/20270101000000_X.cs` }], readSource: () => migration(""), body: "" }).ok, true);

  assert.equal(testExists("LoginEndpointTests.Login_WithValidCredentials_ReturnsAccessTokenAndSetsRefreshCookie"), true);
  assert.equal(testExists("Skarbiec.Identity.Tests.ArchitectureTests.RequestTypes_NeverExposeSettable_UserId"), true);
  assert.equal(testExists("LoginEndpointTests.NoSuchMethod"), false);
  assert.equal(testExists("NoSuchTests.Login_WithValidCredentials_ReturnsAccessTokenAndSetsRefreshCookie"), false);
  assert.equal(testExists("OnlyOnePart"), false);

  assert.deepEqual(consentProblems("## Goal\n\nx\n"), []);
  assert.equal(consentProblems("## Utrata danych\n\n- Portfolio.X\n").length, 1);
  assert.equal(consentProblems("## Przeniesienie danych\n\n- Portfolio.X moves\n").length, 1);
  assert.deepEqual(consentProblems("## Utrata danych\n\n- Portfolio.X\n\nZgoda: ok\n\n## Przeniesienie danych\n\n- Portfolio.Y proof: A.B\n"), []);
});

test("repo fixtures and table names", () => {
  const read = (rel) => readFileSync(path.join(REPO_ROOT, rel), "utf8");
  const portfolio = "services/Portfolio/Skarbiec.Portfolio/Migrations";

  const removed = findDestructive(read(`${portfolio}/20261022140947_RemoveOldAllocationModel.cs`), "Portfolio").map((f) => f.key);
  assert.deepEqual(removed.sort(), [
    "Portfolio.AllocationBalanceOverrides",
    "Portfolio.AllocationBuckets",
    "Portfolio.AllocationPlanEntries",
    "Portfolio.AllocationPlanVersions",
    "Portfolio.PlanningSettings.MinimumTransferPln",
    "Portfolio.TargetAllocationReplicas",
    "Portfolio.TransferPlanBatches",
    "Portfolio.TransferPlanRows",
    "Portfolio.TransferPlans",
  ]);

  assert.deepEqual(findDestructive(read(`${portfolio}/20261016223644_AddAllocationCushion.cs`), "Portfolio"), []);

  const config = JSON.parse(readFileSync(path.join(REPO_ROOT, "scripts", "protected-data.json"), "utf8"));
  assert.deepEqual(Object.keys(config).sort(), ["Identity", "MarketData", "Portfolio", "Reporting"]);
  for (const [service, entry] of Object.entries(config)) {
    const snapshot = read(`services/${service}/Skarbiec.${service}/Migrations/${service}DbContextModelSnapshot.cs`);
    const tables = new Set([...snapshot.matchAll(/ToTable\("(\w+)"/g)].map((m) => m[1]));
    const named = [...(entry.protected === "*" ? [] : entry.protected), ...entry.derived];
    for (const table of named) assert.ok(tables.has(table), `${service}.${table} is in no snapshot`);
  }
});

test("every existing migration scans without throwing", () => {
  for (const service of ["Identity", "Portfolio", "MarketData", "Reporting"]) {
    const dir = path.join(REPO_ROOT, "services", service, `Skarbiec.${service}`, "Migrations");
    for (const f of readdirSync(dir).filter((n) => n.endsWith(".cs") && !n.endsWith(".Designer.cs") && !n.endsWith("ModelSnapshot.cs"))) {
      assert.ok(Array.isArray(findDestructive(readFileSync(path.join(dir, f), "utf8"), service)), f);
    }
  }
});
