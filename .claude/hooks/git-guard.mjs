#!/usr/bin/env node
// PreToolUse(Bash) guard: restricts mutating git/gh commands to the /praca lane.
//
// This hook only ever *narrows* permissions. It emits "ask" or "deny" and never "allow" — when a
// command is inside the lane it prints nothing at all, which leaves the normal `permissions` rules
// in settings.json fully in charge. It cannot grant anything those rules do not already grant.
//
// Why it exists: /praca needs git add|commit|push and gh pr on the `allow` list so a run does not
// stop on a prompt every task. That allowance is repo-wide, so on its own it would also cover a
// silent commit straight onto master in any session. This hook puts those cases back on `ask`.

import { readFileSync } from "node:fs";
import { execFileSync } from "node:child_process";

const LANE_BASE = /^praca_\d{4}-\d{2}-\d{2}$/; // integration branch created by /praca
const LANE_TASK = /^[MT]\d+\.\d+-/; // per-task branch, e.g. M1.3-Portfolio--constrain-currency…
const TRUNK = /^(master|main)$/;

const ASK = "ask";
const DENY = "deny";

function main() {
  const payload = readPayload();
  if (!payload || payload.tool_name !== "Bash") return;

  const command = payload.tool_input?.command;
  if (typeof command !== "string" || !command.trim()) return;

  const verdict = decide(command, payload.cwd || process.cwd());
  if (!verdict) return; // in-lane or irrelevant — say nothing, let settings.json decide

  process.stdout.write(
    JSON.stringify({
      hookSpecificOutput: {
        hookEventName: "PreToolUse",
        permissionDecision: verdict.decision,
        permissionDecisionReason: verdict.reason,
      },
    }),
  );
}

function readPayload() {
  try {
    return JSON.parse(readFileSync(0, "utf8"));
  } catch {
    return null;
  }
}

// Most restrictive segment wins; deny beats ask beats silence.
function decide(command, cwd) {
  let worst = null;

  for (const segment of splitSegments(command)) {
    const verdict = judge(segment, cwd);
    if (!verdict) continue;
    if (!worst || verdict.decision === DENY) worst = verdict;
  }

  return worst;
}

// Split on shell operators only. Quoted text is left intact so a commit message containing `;`
// or `&&` cannot manufacture an extra segment.
function splitSegments(command) {
  const segments = [];
  let current = "";
  let quote = null;

  for (let i = 0; i < command.length; i++) {
    const ch = command[i];

    if (quote) {
      current += ch;
      if (ch === quote && command[i - 1] !== "\\") quote = null;
      continue;
    }
    if (ch === '"' || ch === "'") {
      quote = ch;
      current += ch;
      continue;
    }
    if (ch === ";" || ch === "\n" || ch === "|" || ch === "&") {
      segments.push(current);
      current = "";
      if (command[i + 1] === ch) i++; // swallow the second char of `&&` / `||`
      continue;
    }
    current += ch;
  }
  segments.push(current);

  return segments.map((s) => s.trim()).filter(Boolean);
}

function tokenize(segment) {
  const tokens = segment.match(/"[^"]*"|'[^']*'|\S+/g) ?? [];
  return tokens.map((t) =>
    (t.startsWith('"') && t.endsWith('"')) || (t.startsWith("'") && t.endsWith("'"))
      ? t.slice(1, -1)
      : t,
  );
}

function judge(segment, cwd) {
  const [bin, ...rest] = tokenize(segment);
  if (!rest.length) return null;
  if (bin === "git") return judgeGit(rest, cwd, segment);
  if (bin === "gh") return judgeGh(rest, cwd, segment);
  return null;
}

function judgeGit(args, cwd, segment) {
  const sub = args.find((a) => !a.startsWith("-"));

  if (sub === "commit" || sub === "merge") return outsideLane(cwd, `\`git ${sub}\``);

  if (sub === "push") {
    const forced = args.some(
      (a) => a === "--force" || a === "-f" || a.startsWith("--force-with-lease"),
    );
    // `git push origin master`, `git push origin HEAD:master`, `git push origin +master`
    const trunkRef = args
      .filter((a) => !a.startsWith("-") && a !== "push")
      .some((ref) => TRUNK.test((ref.includes(":") ? ref.split(":").pop() : ref).replace(/^\+/, "")));

    if (trunkRef) {
      return forced
        ? {
            decision: DENY,
            reason: `Force-pushing the trunk is never part of this workflow: ${segment}`,
          }
        : {
            decision: ASK,
            reason: "Pushing to master/main is a human decision, not a /praca step.",
          };
    }
    return outsideLane(cwd, "`git push`");
  }

  return null;
}

function judgeGh(args, cwd, segment) {
  if (args[0] !== "pr") return null;

  if (args[1] === "create") {
    const i = args.indexOf("--base");
    const base = i === -1 ? null : args[i + 1];
    if (base && LANE_BASE.test(base)) return null;
    return {
      decision: ASK,
      reason: base
        ? `PR would target \`${base}\`, outside the /praca lane.`
        : "PR has no explicit --base; it would default to the repository's default branch.",
    };
  }

  if (args[1] === "merge") {
    const base = resolvePrBase(args.slice(2), cwd);
    if (base && LANE_BASE.test(base)) return null;
    return {
      decision: ASK,
      reason: base
        ? `PR merges into \`${base}\`, outside the /praca lane.`
        : `Could not determine the PR's base branch for: ${segment}`,
    };
  }

  return null;
}

function resolvePrBase(mergeArgs, cwd) {
  const selector = mergeArgs.find((a) => !a.startsWith("-"));
  const args = ["pr", "view"];
  if (selector) args.push(selector);
  args.push("--json", "baseRefName", "-q", ".baseRefName");

  return run("gh", args, cwd);
}

function outsideLane(cwd, what) {
  const branch = currentBranch(cwd);

  if (!branch) return { decision: ASK, reason: `Could not determine the current branch for ${what}.` };
  if (LANE_BASE.test(branch) || LANE_TASK.test(branch)) return null; // in lane — no opinion

  return {
    decision: ASK,
    reason: `${what} on \`${branch}\`, outside the /praca lane (praca_<date> or a task branch).`,
  };
}

function currentBranch(cwd) {
  const branch = run("git", ["rev-parse", "--abbrev-ref", "HEAD"], cwd);
  return branch === "HEAD" ? null : branch; // detached
}

function run(bin, args, cwd) {
  try {
    return execFileSync(bin, args, {
      cwd,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "ignore"],
      timeout: 15_000,
    }).trim();
  } catch {
    return null;
  }
}

main();
