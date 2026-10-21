// Usage: imported by autobuild.mjs - wake-timer state, the confirmed sleep guard, suspend/hibernate with a resume timer, the timed Tak/Nie popup; runs nothing on import
import { spawn, spawnSync } from "node:child_process";

const HINT = "run `powercfg /setacvalueindex SCHEME_CURRENT SUB_SLEEP RTCWAKE 1`, then `powercfg /setactive SCHEME_CURRENT`";
const FILETIME_EPOCH_MS = 11644473600000n;
const POPUP_TEXT = { suspend: "Uśpienie za", hibernate: "Hibernacja za", shutdown: "Wyłączenie za" };

// The AC index is the first of the last two hex values of the query; the labels around it are localized
export function parseWakeTimers({ rtcwake, available }) {
  const hex = String(rtcwake).match(/0x[0-9a-f]{8}/gi) ?? [];
  const acIndex = hex.length >= 2 ? Number.parseInt(hex[hex.length - 2], 16) : null;
  const [states] = String(available).split(/nie\s+s.\s+dost.pne|not available/i);
  const sleepState = /\(S[03]\)/.test(states);
  const hibernateAvailable = /hibernacja|hibernate/i.test(states);
  const works = acIndex === 1 && sleepState;
  let hint = null;
  if (!works) {
    if (acIndex === null) hint = "powercfg /query SCHEME_CURRENT SUB_SLEEP RTCWAKE could not be read";
    else if (!sleepState) hint = "powercfg /a lists no S3 or S0 sleep state";
    else hint = `wake timers are not enabled on mains power (AC index ${acIndex}): ${HINT}`;
  }
  return { works, acIndex, hibernateAvailable, hint };
}

// powercfg prints in the OEM code page; latin1 keeps one character per byte, which the patterns above rely on
const powercfg = (args) => {
  const res = spawnSync("powercfg", args, { encoding: "latin1", windowsHide: true });
  return res.error || res.status !== 0 ? "" : res.stdout;
};

export function wakeTimers() {
  if (process.platform !== "win32") return { works: false, acIndex: null, hibernateAvailable: false, hint: "not Windows" };
  return parseWakeTimers({ rtcwake: powercfg(["/query", "SCHEME_CURRENT", "SUB_SLEEP", "RTCWAKE"]), available: powercfg(["/a"]) });
}

export function suspendScript(date, mode) {
  const due = (BigInt(date.getTime()) + FILETIME_EPOCH_MS) * 10000n;
  const state = mode === "hibernate" ? "Hibernate" : "Suspend";
  return [
    "$sig = '[DllImport(\"kernel32.dll\", SetLastError=true)] public static extern IntPtr CreateWaitableTimer(IntPtr attrs, bool manualReset, string name); [DllImport(\"kernel32.dll\", SetLastError=true)] public static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr argument, bool resume);'",
    "Add-Type -MemberDefinition $sig -Name Timer -Namespace Skarbiec",
    "$timer = [Skarbiec.Timer]::CreateWaitableTimer([IntPtr]::Zero, $true, $null)",
    `[long]$due = ${due}`,
    "if (-not [Skarbiec.Timer]::SetWaitableTimer($timer, [ref]$due, 0, [IntPtr]::Zero, [IntPtr]::Zero, $true)) { exit 1 }",
    "Add-Type -AssemblyName System.Windows.Forms",
    `[System.Windows.Forms.Application]::SetSuspendState([System.Windows.Forms.PowerState]::${state}, $false, $false) | Out-Null`,
  ].join("\n");
}

export function confirmScript(action, seconds = 600) {
  const text = POPUP_TEXT[action];
  return [
    "Add-Type -AssemblyName System.Windows.Forms",
    "$script:answer = 'timeout'",
    `$script:left = ${Math.round(seconds)}`,
    "$form = New-Object System.Windows.Forms.Form",
    "$form.Text = 'Skarbiec'",
    "$form.TopMost = $true",
    "$form.StartPosition = 'CenterScreen'",
    "$form.FormBorderStyle = 'FixedDialog'",
    "$form.ControlBox = $false",
    "$form.ClientSize = New-Object System.Drawing.Size(320, 110)",
    "$label = New-Object System.Windows.Forms.Label",
    "$label.Font = New-Object System.Drawing.Font('Segoe UI', 14)",
    "$label.Location = New-Object System.Drawing.Point(10, 15)",
    "$label.Size = New-Object System.Drawing.Size(300, 35)",
    "$label.TextAlign = 'MiddleCenter'",
    `$show = { $label.Text = '${text} ' + [int][math]::Floor($script:left / 60) + ':' + ($script:left % 60).ToString('00') }`,
    "& $show",
    "$form.Controls.Add($label)",
    "$yes = New-Object System.Windows.Forms.Button",
    "$yes.Text = 'Tak'",
    "$yes.Location = New-Object System.Drawing.Point(40, 65)",
    "$yes.Add_Click({ $script:answer = 'yes'; $form.Close() })",
    "$form.Controls.Add($yes)",
    "$no = New-Object System.Windows.Forms.Button",
    "$no.Text = 'Nie'",
    "$no.Location = New-Object System.Drawing.Point(205, 65)",
    "$no.Add_Click({ $script:answer = 'no'; $form.Close() })",
    "$form.Controls.Add($no)",
    "$timer = New-Object System.Windows.Forms.Timer",
    "$timer.Interval = 1000",
    "$timer.Add_Tick({ $script:left--; if ($script:left -le 0) { $script:answer = 'timeout'; $form.Close() } else { & $show } })",
    "$timer.Start()",
    "$form.Add_Shown({ $form.Activate() })",
    "[void]$form.ShowDialog()",
    "Write-Output $script:answer",
  ].join("\n");
}

const encoded = (script) => Buffer.from(script, "utf16le").toString("base64");

const GUARD_READY_MS = 10e3;
const GUARD_REASSERT_S = 30;

// 0x80000003 = ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED - the display staying on also keeps the idle lock off
export function guardScript(parentPid) {
  return [
    "$sig = '[DllImport(\"kernel32.dll\")] public static extern uint SetThreadExecutionState(uint esFlags);'",
    "Add-Type -MemberDefinition $sig -Name Power -Namespace Skarbiec | Out-Null",
    "$flags = [uint32]2147483651",
    "if ([Skarbiec.Power]::SetThreadExecutionState($flags) -eq 0) { exit 1 }",
    "[Console]::Out.WriteLine('ready')",
    "[Console]::Out.Flush()",
    `while (Get-Process -Id ${parentPid} -ErrorAction SilentlyContinue) { Start-Sleep -Seconds ${GUARD_REASSERT_S}; [void][Skarbiec.Power]::SetThreadExecutionState($flags) }`,
  ].join("\n");
}

export function guardSpawn(parentPid) {
  return {
    command: "powershell.exe",
    args: ["-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand", encoded(guardScript(parentPid))],
    options: { windowsHide: true, stdio: ["ignore", "pipe", "ignore"] },
  };
}

// resolves once the guard child printed `ready` (SetThreadExecutionState succeeded); the child stays up and re-asserts the state
export function startGuard({ parentPid = process.pid, spawnFn = spawn, timeoutMs = GUARD_READY_MS } = {}) {
  return new Promise((resolve) => {
    let settled = false;
    let child = null;
    const settle = (result) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      if (!result.active) child?.kill();
      resolve({ ...result, child });
    };
    const timer = setTimeout(() => settle({ active: false, reason: `no confirmation in ${Math.round(timeoutMs / 1000)} s` }), timeoutMs);
    try {
      const { command, args, options } = guardSpawn(parentPid);
      child = spawnFn(command, args, options);
    } catch (err) {
      settle({ active: false, reason: err.message });
      return;
    }
    let out = "";
    child.stdout?.setEncoding?.("utf8");
    child.stdout?.on("data", (chunk) => {
      out += chunk;
      if (/^ready\s*$/m.test(out)) settle({ active: true, reason: null });
    });
    child.on("error", (err) => settle({ active: false, reason: err.message }));
    child.on("close", (code) => settle({ active: false, reason: `the guard exited with code ${code}` }));
  });
}

export function guardLine(guard) {
  if (guard.active) return "sleep, display-off and idle lock blocked while the run lasts (closing the lid still sleeps)";
  return `UWAGA: blokada snu nie działa (${guard.reason}) — PC może zasnąć w trakcie buildu`;
}

export function suspendUntil(date, mode) {
  return new Promise((resolve) => {
    const child = spawn("powershell.exe", ["-NoProfile", "-NonInteractive", "-WindowStyle", "Hidden", "-EncodedCommand", encoded(suspendScript(date, mode))], { windowsHide: true, stdio: "ignore" });
    child.on("error", () => resolve(false));
    child.on("close", (code) => resolve(code === 0));
  });
}

// windowsHide must stay false: Windows applies it to the child's first ShowWindow, which is the popup itself; -WindowStyle Hidden hides only the console
export function confirmSpawn(action, seconds = 600) {
  return {
    command: "powershell.exe",
    args: ["-NoProfile", "-Sta", "-WindowStyle", "Hidden", "-EncodedCommand", encoded(confirmScript(action, seconds))],
    options: { windowsHide: false, stdio: ["ignore", "pipe", "ignore"] },
  };
}

// "no" when the window cannot be shown at all: nobody saw the question, so nothing is switched off unasked
export function confirmPower(action, seconds = 600) {
  return new Promise((resolve) => {
    let out = "";
    const { command, args, options } = confirmSpawn(action, seconds);
    const child = spawn(command, args, options);
    const backstop = setTimeout(() => {
      child.kill();
      resolve("timeout");
    }, (seconds + 60) * 1000);
    child.stdout.setEncoding("utf8");
    child.stdout.on("data", (chunk) => {
      out += chunk;
    });
    child.on("error", () => {
      clearTimeout(backstop);
      resolve("no");
    });
    child.on("close", () => {
      clearTimeout(backstop);
      const answer = out.trim().split(/\r?\n/).pop();
      resolve(["yes", "no", "timeout"].includes(answer) ? answer : "no");
    });
  });
}
