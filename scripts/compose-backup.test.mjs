import assert from "node:assert/strict";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { test } from "node:test";
import { SERVICES, backupAll, formatBackups, listBackups, resetWithBackup, restoreBackup, upWithBackup } from "./compose-backup.mjs";

const REVISION = "0123456789abcdef0123456789abcdef01234567";
const MIGRATIONS = {
  identity: "20260923133152_InitialCreate",
  portfolio: "20261011121456_AccountsReplacePortfolios",
  marketdata: "20261013133449_InitialCreate",
  reporting: "20261012090000_InitialCreate",
};
const dbOf = (argv) => argv[argv.indexOf("-d") + 1];
const isFullUp = (call) => call.argv.join(" ") === "up -d --wait";
const isPostgresUp = (call) => call.argv.join(" ") === "up -d --wait postgres";
const isDump = (call) => call.argv.includes("pg_dump");
const isDown = (call) => call.argv[0] === "down" && call.argv.includes("--volumes");

function makeCtx(t, options = {}) {
  const root = mkdtempSync(path.join(os.tmpdir(), "compose-backup-"));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const state = { running: options.running ?? true, clock: Date.UTC(2026, 9, 23, 12, 0, 0) };
  const known = options.known ?? Object.fromEntries(SERVICES.map((s) => [s.name, [MIGRATIONS[s.name]]]));
  const calls = [];
  const logs = [];
  const answers = [...(options.answers ?? ["reset"])];

  const compose = (argv, opts = {}) => {
    calls.push({ kind: "compose", argv, opts });
    const joined = argv.join(" ");
    if (joined.startsWith("ps ")) return { ok: true, out: state.running ? "container-id" : "", tail: "", error: "" };
    if (isPostgresUp({ argv })) state.running = true;
    if (argv.includes("pg_dump")) {
      if (options.failDump === dbOf(argv)) return { ok: false, out: "", tail: "connection refused", error: "" };
      writeFileSync(opts.stdoutFile, "PGDMP");
    }
    if (argv.includes("-tAc")) {
      const service = SERVICES.find((s) => s.database === dbOf(argv));
      return { ok: true, out: MIGRATIONS[service.name], tail: "", error: "" };
    }
    if (joined === "config --services") return { ok: true, out: "postgres\nrabbitmq\nidentity\ngateway\n", tail: "", error: "" };
    return { ok: true, out: "", tail: "", error: "" };
  };
  const run = (cmd, argv) => {
    calls.push({ kind: "run", cmd, argv });
    if (cmd === "git" && argv[0] === "ls-tree") {
      if (options.unknownRevision) return { ok: false, out: "", tail: "fatal: Not a valid object name", error: "" };
      const service = SERVICES.find((s) => argv.at(-1).includes(`/${s.dir}/`));
      const files = known[service.name].flatMap((id) => [`${argv.at(-1)}/${id}.cs`, `${argv.at(-1)}/${id}.Designer.cs`]);
      files.push(`${argv.at(-1)}/${service.dir}DbContextModelSnapshot.cs`);
      return { ok: true, out: files.join("\n"), tail: "", error: "" };
    }
    return { ok: true, out: "", tail: "", error: "" };
  };

  const ctx = {
    run,
    compose,
    revisionOf: () => (options.missingImages ? null : REVISION),
    root,
    now: () => new Date((state.clock += 1000)),
    log: (message) => logs.push(message),
    interactive: options.interactive ?? true,
    ask: async () => answers.shift() ?? "",
    volumeExists: () => options.volume ?? true,
  };
  return { ctx, calls, logs, root };
}

const composeCalls = (calls) => calls.filter((c) => c.kind === "compose");

test("up dumps before starting the stack", (t) => {
  const { ctx, calls, root } = makeCtx(t);
  assert.equal(upWithBackup(ctx), 0);

  const compose = composeCalls(calls);
  const dumps = compose.filter(isDump);
  assert.deepEqual(dumps.map((c) => dbOf(c.argv)), SERVICES.map((s) => s.database));
  const [name] = readdirSync(root);
  assert.ok(existsSync(path.join(root, name, "manifest.json")));
  for (const service of SERVICES) assert.ok(existsSync(path.join(root, name, `${service.database}.dump`)));
  assert.ok(compose.findIndex(isFullUp) > compose.findLastIndex(isDump));
  assert.equal(compose.filter(isFullUp).length, 1);
});

test("failed dump stops up", (t) => {
  const { ctx, calls, root } = makeCtx(t, { failDump: "portfolio_db" });
  assert.throws(() => upWithBackup(ctx), /portfolio_db/);

  assert.equal(composeCalls(calls).filter(isFullUp).length, 0);
  assert.deepEqual(readdirSync(root), []);
});

test("first start needs no backup", (t) => {
  const { ctx, calls, root } = makeCtx(t, { volume: false, running: false });
  assert.equal(upWithBackup(ctx), 0);

  const compose = composeCalls(calls);
  assert.equal(compose.filter(isDump).length, 0);
  assert.equal(compose.filter(isPostgresUp).length, 0);
  assert.equal(compose.filter(isFullUp).length, 1);
  assert.equal(readdirSync(root).length, 0);
});

test("starts postgres alone to dump", (t) => {
  const { ctx, calls } = makeCtx(t, { running: false });
  assert.equal(upWithBackup(ctx), 0);

  const compose = composeCalls(calls);
  const postgresUp = compose.findIndex(isPostgresUp);
  assert.ok(postgresUp >= 0);
  assert.ok(postgresUp < compose.findIndex(isDump));
  assert.ok(compose.findIndex(isFullUp) > compose.findLastIndex(isDump));
});

test("keeps the five newest", (t) => {
  const { ctx, root } = makeCtx(t);
  const old = ["20260101-000001", "20260101-000002", "20260101-000003", "20260101-000004", "20260101-000005"];
  for (const name of old) {
    mkdirSync(path.join(root, name));
    writeFileSync(path.join(root, name, "manifest.json"), JSON.stringify({ name, createdAt: "2026-01-01T00:00:00.000Z", reason: "old", databases: {} }));
  }

  const made = backupAll(ctx, { reason: "sixth" });

  const remaining = readdirSync(root).sort();
  assert.equal(remaining.length, 5);
  assert.deepEqual(remaining, [...old.slice(1), made.name]);
});

test("reset backs up and asks", async (t) => {
  await t.test("typed confirmation runs down --volumes after the dumps", async () => {
    const { ctx, calls } = makeCtx(t, { answers: ["reset"] });
    assert.equal(await resetWithBackup(ctx), 0);
    const compose = composeCalls(calls);
    assert.equal(compose.filter(isDump).length, 4);
    assert.ok(compose.findIndex(isDown) > compose.findLastIndex(isDump));
  });

  await t.test("a different answer cancels", async () => {
    const { ctx, calls } = makeCtx(t, { answers: ["yes"] });
    await assert.rejects(resetWithBackup(ctx), /cancelled/);
    assert.equal(composeCalls(calls).filter(isDown).length, 0);
  });

  await t.test("a non-interactive run refuses without --yes and proceeds with it", async () => {
    const refused = makeCtx(t, { interactive: false });
    await assert.rejects(resetWithBackup(refused.ctx), /--yes/);
    assert.equal(composeCalls(refused.calls).filter(isDown).length, 0);

    const forced = makeCtx(t, { interactive: false });
    assert.equal(await resetWithBackup(forced.ctx, { yes: true }), 0);
    assert.equal(composeCalls(forced.calls).filter(isDown).length, 1);
  });

  await t.test("a failed backup prevents the reset", async () => {
    const { ctx, calls } = makeCtx(t, { failDump: "identity_db" });
    await assert.rejects(resetWithBackup(ctx, { yes: true }), /identity_db/);
    assert.equal(composeCalls(calls).filter(isDown).length, 0);
  });
});

test("restore compatibility", async (t) => {
  const restoreCalls = (calls) => composeCalls(calls).filter((c) => c.argv.includes("pg_restore"));

  await t.test("an older dump restores every database and warns", () => {
    const newer = Object.fromEntries(SERVICES.map((s) => [s.name, [MIGRATIONS[s.name], "29990101000000_Later"]]));
    const { ctx, calls, logs } = makeCtx(t, { known: newer });
    backupAll(ctx, { reason: "x" });
    calls.length = 0;

    restoreBackup(ctx, {});

    const restores = restoreCalls(calls);
    assert.deepEqual(restores.map((c) => dbOf(c.argv)), SERVICES.map((s) => s.database));
    for (const restore of restores) assert.ok(restore.opts.stdinFile.endsWith(".dump"));
    assert.ok(logs.some((line) => line.startsWith("warning:") && line.includes("older")));
    const compose = composeCalls(calls);
    const stop = compose.find((c) => c.argv[0] === "stop");
    assert.deepEqual(stop.argv.slice(1), ["rabbitmq", "identity", "gateway"]);
    assert.ok(compose.some((c) => c.argv.join(" ").includes("DROP DATABASE IF EXISTS \"identity_db\"")));
    assert.ok(compose.some((c) => c.argv.join(" ").includes('CREATE DATABASE "identity_db" OWNER "identity_user"')));
    assert.ok(compose.some((c) => c.argv.join(" ").includes("pg_terminate_backend")));
  });

  await t.test("a dump with a migration unknown to the images is refused unless forced", () => {
    const imagesOnly = Object.fromEntries(SERVICES.map((s) => [s.name, ["20200101000000_Ancient"]]));
    const { ctx, calls } = makeCtx(t, { known: imagesOnly });
    backupAll(ctx, { reason: "x" });
    calls.length = 0;

    assert.throws(() => restoreBackup(ctx, {}), /unknown to the images/);
    assert.equal(restoreCalls(calls).length, 0);
    assert.equal(composeCalls(calls).filter((c) => c.argv[0] === "stop").length, 0);

    restoreBackup(ctx, { force: true });
    assert.equal(restoreCalls(calls).length, 4);
  });

  await t.test("missing images or a revision unknown to git are refused unless forced", () => {
    for (const options of [{ missingImages: true }, { unknownRevision: true }]) {
      const { ctx, calls } = makeCtx(t, options);
      backupAll(ctx, { reason: "x" });
      calls.length = 0;

      assert.throws(() => restoreBackup(ctx, { name: "latest" }), /refused/);
      assert.equal(restoreCalls(calls).length, 0);
      restoreBackup(ctx, { force: true });
      assert.equal(restoreCalls(calls).length, 4);
    }
  });

  await t.test("an unknown backup name fails", () => {
    const { ctx } = makeCtx(t);
    backupAll(ctx, { reason: "x" });
    assert.throws(() => restoreBackup(ctx, { name: "19990101-000000" }), /no backup named/);
  });
});

test("manifest and listing", (t) => {
  const { ctx, root } = makeCtx(t);
  const made = backupAll(ctx, { reason: "before the experiment" });

  const manifest = JSON.parse(readFileSync(path.join(root, made.name, "manifest.json"), "utf8"));
  assert.equal(manifest.reason, "before the experiment");
  assert.ok(!Number.isNaN(Date.parse(manifest.createdAt)));
  for (const service of SERVICES) {
    assert.equal(manifest.databases[service.name].latestMigration, MIGRATIONS[service.name]);
    assert.equal(manifest.databases[service.name].file, `${service.database}.dump`);
  }

  backupAll(ctx, { reason: "second" });
  const backups = listBackups(ctx);
  assert.equal(backups.length, 2);
  assert.equal(backups[0].manifest.reason, "second");
  const lines = formatBackups(backups);
  assert.equal(lines.length, 2);
  assert.ok(lines[1].includes(made.name) && lines[1].includes("before the experiment") && lines[1].includes(MIGRATIONS.portfolio));
  assert.deepEqual(formatBackups([]), ["no backups"]);
});

test("backup without a running postgres refuses", (t) => {
  const { ctx } = makeCtx(t, { running: false });
  assert.throws(() => backupAll(ctx, { reason: "x" }), /not running/);
});
