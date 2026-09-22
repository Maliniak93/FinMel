---
name: ops-playbook
description: Git/PR/CI/dependabot workflow the ops agent owns - branch model, commit and PR shape, CI triage, dependabot rules.
user-invocable: false
---

# Ops playbook
You are the only agent that runs git and `gh` mutations. Everyone else's git/gh commands are read-only.

## Branches
- Feature/spec work: `feat/<slug>` off `master`, where `<slug>` matches the spec's `branch` frontmatter.
- Tooling-only changes: `chore/<slug>` or `fix/<slug>`.
- **Never commit directly on `master`.** If you find yourself there with staged work, branch first.

## A `/build` run calls you three times
Do only the step you were asked for — the workflow script owns the order.

1. **Branch** (before anything is written): tree clean apart from the spec file → `git fetch origin` → `git switch -c feat/<slug> master`. Already on that branch = resumed run, stay. Anything unrelated in the tree = stop and name it. No commit.
2. **Freeze** (after a green verify, before the review): `git add -A` + commit with the spec title, amending your own freeze commit from this run rather than stacking a second. No push. This is what makes the review honest — `git diff` alone never shows a new file.
3. **Ship**: push `-u`, `gh pr create --base master`, set the spec to `status: done` with the PR URL under `## Result` as a second pushed commit, then post the run's minor findings on the PR.

## Commit and PR
1. `git status --porcelain` first. Everything in the tree must belong to this change — an unrelated file is a stop: report it by name instead of sweeping it into the commit.
2. Commit message subject = the spec's title (or the chore's one-line description); trailer: `Co-Authored-By: Claude <noreply@anthropic.com>`. One commit per run — amend rather than stack a second commit for the same run. The exception is the spec's own `status: done` edit, which lands after the push and therefore as its own commit.
3. `git push -u origin <branch>`.
4. `gh pr create --base master --head <branch> --title "<spec title>" --body "<what changed, why, how it was verified, and the spec path>"`. If a PR already exists for the branch, push and report its URL instead of opening a second one.
5. On shipping a spec: edit its file to set `status: done` and append the PR URL under a `## Result` heading (create the heading if missing).

## Review comments on a PR (GitHub MCP)
Minor findings from a build run are posted once the PR exists, as a single review: `pull_request_review_write` (method `create`) → one `add_comment_to_pending_review` per finding at its `file`/`line` → `pull_request_review_write` (method `submit_pending`) with event `COMMENT`. Never `APPROVE` or `REQUEST_CHANGES` — the review is a record for the user, not a verdict. Findings without a file or line go into the review body; an empty list means post nothing.

## Hard limits
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
