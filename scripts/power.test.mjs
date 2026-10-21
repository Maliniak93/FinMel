import assert from "node:assert/strict";
import { test } from "node:test";
import { EventEmitter } from "node:events";
import { confirmScript, confirmSpawn, guardLine, guardScript, parseWakeTimers, startGuard, suspendScript } from "./power.mjs";

function fakeGuard(behave) {
  const child = new EventEmitter();
  child.stdout = new EventEmitter();
  child.killed = false;
  child.kill = () => {
    child.killed = true;
  };
  setImmediate(() => behave(child));
  return child;
}
const guardWith = (behave, timeoutMs = 30) => startGuard({ parentPid: 1, timeoutMs, spawnFn: () => fakeGuard(behave) });

const rtcwake = (ac, dc = 1) => `GUID podgrupy: 238c9fa8-0aad-41ed-83f4-97be242c8f20  (Uśpienie)
  GUID ustawienia zasilania: bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d  (Zezwalaj na czasomierze wznawiania)
    Możliwe ustawienia indeksu: 000
    Przyjazna nazwa możliwego ustawienia: Wyłącz
    Możliwe ustawienia indeksu: 001
    Przyjazna nazwa możliwego ustawienia: Włącz
    Możliwe ustawienia indeksu: 002
    Przyjazna nazwa możliwego ustawienia: Tylko ważne czasomierze wznawiania
  Bieżący indeks ustawienia zasilania prądem przemiennym: 0x0000000${ac}
  Bieżący indeks ustawienia zasilania prądem stałym: 0x0000000${dc}
`;

const withHibernate = `Na tym systemie są dostępne następujące stany uśpienia:
    Wstrzymanie (S3)
    Hibernacja
    Szybkie uruchamianie

Następujące stany uśpienia nie są dostępne w tym systemie:
    Czuwanie (S1)
        Oprogramowanie układowe systemu nie obsługuje tego stanu gotowości.
`;

const withoutHibernate = `Na tym systemie są dostępne następujące stany uśpienia:
    Wstrzymanie (S3)

Następujące stany uśpienia nie są dostępne w tym systemie:
    Hibernacja
        Hibernacja nie została włączona.
`;

test("wake timers parsed from localized output", () => {
  const off = parseWakeTimers({ rtcwake: rtcwake(2), available: withHibernate });
  assert.equal(off.works, false);
  assert.equal(off.hibernateAvailable, true);
  assert.match(off.hint, /powercfg \/setacvalueindex SCHEME_CURRENT SUB_SLEEP RTCWAKE 1/);
  assert.match(off.hint, /powercfg \/setactive SCHEME_CURRENT/);

  const on = parseWakeTimers({ rtcwake: rtcwake(1, 0), available: withHibernate });
  assert.equal(on.works, true);
  assert.equal(on.hibernateAvailable, true);

  const noHibernate = parseWakeTimers({ rtcwake: rtcwake(1), available: withoutHibernate });
  assert.equal(noHibernate.works, true);
  assert.equal(noHibernate.hibernateAvailable, false);
});

test("power scripts use a resume timer and a timed popup", () => {
  const at = new Date(2026, 9, 19, 3, 0, 0);
  const suspend = suspendScript(at, "suspend");
  const hibernate = suspendScript(at, "hibernate");
  assert.match(suspend, /CreateWaitableTimer/);
  assert.match(suspend, /SetWaitableTimer/);
  assert.match(suspend, /SetSuspendState\(\S*Suspend/);
  assert.match(hibernate, /SetSuspendState\(\S*Hibernate/);
  for (const script of [suspend, hibernate]) {
    assert.doesNotMatch(script, /rundll32/i);
    assert.doesNotMatch(script, /waketimers/i);
  }

  const popup = confirmScript("shutdown", 600);
  assert.match(popup, /TopMost/i);
  assert.match(popup, /600/);
  assert.match(popup, /Tak/);
  assert.match(popup, /Nie/);
  assert.match(popup, /Wyłączenie za/);
  assert.match(confirmScript("suspend", 600), /Uśpienie za/);
  assert.match(confirmScript("hibernate", 600), /Hibernacja za/);
});

test("guard reports active only after ready", async () => {
  const ready = await guardWith((child) => child.stdout.emit("data", "ready\r\n"));
  assert.equal(ready.active, true);
  assert.match(guardLine(ready), /blocked while the run lasts/);

  const split = await guardWith((child) => {
    child.stdout.emit("data", "rea");
    child.stdout.emit("data", "dy\n");
  });
  assert.equal(split.active, true);

  const exited = await guardWith((child) => child.emit("close", 1));
  assert.equal(exited.active, false);
  assert.match(exited.reason, /code 1/);

  const failed = await guardWith((child) => child.emit("error", new Error("spawn powershell.exe ENOENT")));
  assert.equal(failed.active, false);
  assert.match(failed.reason, /ENOENT/);

  const noise = await guardWith((child) => child.stdout.emit("data", "not ready yet\n"));
  assert.equal(noise.active, false);
  assert.match(noise.reason, /no confirmation/);

  const silent = await guardWith(() => {});
  assert.equal(silent.active, false);
  assert.match(silent.reason, /no confirmation/);
  assert.equal(silent.child.killed, true);

  const warning = guardLine(silent);
  assert.match(warning, /^UWAGA: blokada snu nie działa \(.+\) — PC może zasnąć w trakcie buildu$/);
  assert.doesNotMatch(warning, /blocked while the run lasts/);
});

test("the guard script re-asserts the state and prints ready after success", () => {
  const script = guardScript(4242);
  assert.match(script, /SetThreadExecutionState/);
  assert.match(script, /2147483651/);
  assert.ok(script.indexOf("WriteLine('ready')") > script.indexOf("-eq 0) { exit 1 }"));
  assert.match(script, /Get-Process -Id 4242/);
  assert.match(script, /Start-Sleep -Seconds 30/);
  assert.doesNotMatch(script, /powercfg/i);
});

test("the popup is spawned visible, only its console hidden", () => {
  const { command, args, options } = confirmSpawn("shutdown", 600);
  assert.equal(command, "powershell.exe");
  assert.notEqual(options.windowsHide, true);
  assert.ok(args.join(" ").includes("-WindowStyle Hidden"));
  assert.ok(args.includes("-Sta"));
});
