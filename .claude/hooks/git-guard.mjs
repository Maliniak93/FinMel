#!/usr/bin/env node
// PreToolUse(Bash) guard: restricts mutating git/gh commands to a working lane.
//
// This hook only ever *narrows* permissions. It emits "ask" or "deny" and never "allow" — when a
// command is inside a lane it prints nothing at all, which leaves the normal `permissions` rules
// in settings.json fully in charge. It cannot grant anything those rules do not already grant.
//
// Why it exists: automated workflows need git add|commit|push and gh pr on the `allow` list so a
// run does not stop on a prompt every step. That allowance is repo-wide, so on its own it would
// also cover a silent commit straight onto master in any session. This hook puts those cases back
// on `ask` (or `deny` for a force-push to trunk), no matter which lane produced them.
//
// Lanes — branches where commit / push / PR-to-trunk proceed without a prompt:
//   - `feat/*`, `chore/*`, `fix/*` — the current model. `/build`'s `ops` agent commits, pushes and
//     opens a PR to master from `feat/<slug>` for every spec it ships; the user still merges (see
//     below).
//   - `praca_YYYY-MM-DD` and `[MT]<n>.<n>-...` — legacy branches from the earlier /praca workflow,
//     kept so old branches and open PRs keep behaving the way they always did.
//
// Merging is never silent: `gh pr merge` always asks, regardless of branch or base — that decision
// stays with the user. Pushing straight to master/main always asks; force-pushing it is denied
// outright. Commit/push/PR activity outside every lane above falls back to asking, same as anything
// else this hook doesn't specifically recognize.

import { readFileSync } from "node:fs";
import { execFileSync } from "node:child_process";

const LANE_CONVENTIONAL = /^(feat|chore|fix)\//; // current model; /build's ops agent lives here
const LANE_PRACA = /^praca_\d{4}-\d{2}-\d{2}$/; // legacy: integration branch created by /praca
const LANE_TASK = /^[MT]\d+\.\d+-/; // legacy: per-task branch, e.g. M1.3-Portfolio--constrain-currency…
const TRUNK = /^(master|main)$/;

function isLane(branch) {
  return !!branch && (LANE_CONVENTIONAL.test(branch) || LANE_PRACA.test(branch) || LANE_TASK.test(branch));
}

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
            reason: "Pushing to master/main is a human decision, not an automated step.",
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
    const branch = currentBranch(cwd);

    if (!base) {
      return {
        decision: ASK,
        reason: "PR has no explicit --base; it would default to the repository's default branch.",
      };
    }
    if (TRUNK.test(base) && isLane(branch)) return null; // lane -> master/main is the expected flow

    return {
      decision: ASK,
      reason: TRUNK.test(base)
        ? `\`gh pr create --base ${base}\` from \`${branch ?? "an indeterminate branch"}\`, which is not a lane branch (feat/*, chore/*, fix/*).`
        : `PR would target \`${base}\`, not master/main.`,
    };
  }

  if (args[1] === "merge") {
    // Always a human decision, regardless of branch, base, or how the PR was opened.
    return { decision: ASK, reason: "Merging is the user's decision" };
  }

  return null;
}

function outsideLane(cwd, what) {
  const branch = currentBranch(cwd);

  if (!branch) return { decision: ASK, reason: `Could not determine the current branch for ${what}.` };
  if (isLane(branch)) return null; // in lane — no opinion

  return {
    decision: ASK,
    reason: `${what} on \`${branch}\`, outside every working lane (feat/*, chore/*, fix/*, praca_<date>, or a task branch).`,
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
