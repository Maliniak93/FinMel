#!/usr/bin/env node

import { readFileSync } from "node:fs";
import { execFileSync } from "node:child_process";

const LANE_CONVENTIONAL = /^(feat|chore|fix)\//;
const LANE_PRACA = /^praca_\d{4}-\d{2}-\d{2}$/;
const LANE_TASK = /^[MT]\d+\.\d+-/;
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
  if (!verdict) return;

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

function decide(command, cwd) {
  let worst = null;

  for (const segment of splitSegments(command)) {
    const verdict = judge(segment, cwd);
    if (!verdict) continue;
    if (!worst || verdict.decision === DENY) worst = verdict;
  }

  return worst;
}

// Quoted text stays intact, so a commit message containing `;` or `&&` cannot manufacture a segment.
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
      if (command[i + 1] === ch) i++;
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
    if (TRUNK.test(base) && isLane(branch)) return null;

    return {
      decision: ASK,
      reason: TRUNK.test(base)
        ? `\`gh pr create --base ${base}\` from \`${branch ?? "an indeterminate branch"}\`, which is not a lane branch (feat/*, chore/*, fix/*).`
        : `PR would target \`${base}\`, not master/main.`,
    };
  }

  if (args[1] === "merge") {
    return { decision: ASK, reason: "Merging is the user's decision" };
  }

  return null;
}

function outsideLane(cwd, what) {
  const branch = currentBranch(cwd);

  if (!branch) return { decision: ASK, reason: `Could not determine the current branch for ${what}.` };
  if (isLane(branch)) return null;

  return {
    decision: ASK,
    reason: `${what} on \`${branch}\`, outside every working lane (feat/*, chore/*, fix/*, praca_<date>, or a task branch).`,
  };
}

function currentBranch(cwd) {
  const branch = run("git", ["rev-parse", "--abbrev-ref", "HEAD"], cwd);
  return branch === "HEAD" ? null : branch;
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
