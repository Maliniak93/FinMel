---
name: ops-playbook
description: Git/PR/CI/dependabot workflow the ops agent owns - branch model, commit and PR shape, CI triage, dependabot rules.
user-invocable: false
---

# Ops playbook
You are the only agent that runs git and `gh` mutations. Everyone else's git/gh commands are read-only.

## Branches
- Spec work (`/build`, `/fix`): the branch the message names — the issue's Branch field, `feat/<slug>` or `fix/<slug>` — off `master`.
- Tooling-only changes: `chore/<slug>` or `fix/<slug>`.
- **Never commit directly on `master`.** If you find yourself there with staged work, branch first.

## A `/build` run calls you three times — and never commits
The whole pipeline stops at `git add`. The commit, the push and the PR are the user's, done by hand after the run reports. Do only the step you were asked for — the workflow script owns the order.

1. **Branch** (before anything is written): tree clean (the spec copy is gitignored) → `git fetch origin` → `gh issue develop <n> --name <issue branch> --base master --checkout` (the issue's linked branch: merging its PR closes the issue, no `Closes` needed; never `git switch -c`, and `git switch` if `<issue branch>` already exists locally or on origin). Already on that branch = resumed run, stay. Anything unrelated in the tree = stop and name it. No commit.
2. **Stage** (after a green verify, before the review): `git add -A`, nothing else. This is what makes the review honest — the reviewer diffs `git diff --cached`, and a bare `git diff` never shows a new file.
3. **Stage (final)**: `git add -A` again, then run the `gh-project.mjs report` command from the message verbatim (it posts the run report on the issue). A blocked run sends that command alone. No push, no `gh pr create`, and no PR review comments — there is no PR. The minor findings reach the issue through that report.

## Commit and PR — only in a chore the user asked for directly
Never inside a build run. In an explicit `/ops <task>`:
1. `git status --porcelain` first. Everything in the tree must belong to this change — an unrelated file is a stop: report it by name instead of sweeping it into the commit.
2. Commit message subject = the chore's one-line description; trailer: `Co-Authored-By: Claude <noreply@anthropic.com>`.
3. `git push -u origin <branch>`.
4. `gh pr create --base master --head <branch> --title "<title>" --body "<what changed, why, how it was verified>"`. If a PR already exists for the branch, push and report its URL instead of opening a second one.

## Review comments on a PR (GitHub MCP)
Only for a PR that already exists and a task that asked for it: `pull_request_review_write` (method `create`) → one `add_comment_to_pending_review` per finding at its `file`/`line` → `pull_request_review_write` (method `submit_pending`) with event `COMMENT`. Never `APPROVE` or `REQUEST_CHANGES` — the review is a record for the user, not a verdict. Findings without a file or line go into the review body; an empty list means post nothing.

## Hard limits
- **Inside a build run, `git add` is the only mutation you make.** No commit, no push, no PR — however finished and green the work looks.
- **Never merge** — `gh pr merge` is the user's call, always; don't even ask an agent to do it.
- Never push to `master`, never force-push, never `git rebase -i`, never delete a branch that isn't the one you're finishing.
- `.claude/hooks/git-guard.mjs` allows commit/push on `feat/*`/`chore/*`/`fix/*` and `gh pr create --base master` silently; anything else surfaces a permission prompt. A prompt appearing mid-task means the command is aiming outside the lane — **read it, don't click through it**, and don't reword the command to dodge it.
- The guard hooks `Bash` only. GitHub MCP tools are for reads and review comments; every repository state change (branch, commit, push, PR create/close) goes through `git`/`gh` in Bash so the guard can see it.

## CI
- Runner is self-hosted on WSL2 (`deploy/ci-runner-setup.md`) — jobs queue and run one at a time, and can take minutes to pick up.
- `gh pr checks <n> --watch`. On red: `gh run view --log-failed` and hand the concrete failure to whoever owns the fix — never edit production code, tests, or `.claude/rules/*` yourself just to turn CI green.
- If jobs stay `Queued` for an extended period, the runner is likely offline — **report this to the user, don't retry or re-run the workflow.**

## Dependabot (`.github/dependabot.yml`)
- MassTransit **major** version bump → close the PR with a comment citing ADR-012 (pins MassTransit v8).
- Any other **major** bump → leave it open; summarize the risk for the user in one line.
- **Minor/patch** groups → report whether CI is green or red; the user decides whether to merge.

## Branch cleanup
After a PR is merged (by the user): `git branch -d <branch>`, and `git push origin --delete <branch>` — only once the PR is actually merged, never before.

## Anything else
CI workflow edits, dependabot config changes, self-hosted runner notes, `gh run` inspection are yours (`.github/**`, `deploy/**`, `.gitattributes`, `.gitignore`). Anything long-lived on the user's own machine — installing or registering a runner, handling credentials — gets written up as instructions, not done by you.
