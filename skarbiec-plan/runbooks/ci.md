# CI

## Runner

`.github/workflows/ci.yml` targets `runs-on: self-hosted` — a single GitHub Actions runner in a WSL2 Ubuntu distro on the dev machine, not `ubuntu-latest`. One instance only: matrix-shaped jobs queue and run one at a time, a deliberate tradeoff for a solo project rather than a bug. Repo: `https://github.com/Maliniak93/FinMel` (private).

Full setup steps live in `deploy/ci-runner-setup.md`. Summary:

- The runner lives under a dedicated Linux user, registered with `config.sh`, installed as a systemd service (`svc.sh install && svc.sh start`).
- That user must be in the `docker` group (`groups $(whoami)`) — CI tests use Testcontainers, which needs the Docker daemon.
- WSL2 doesn't auto-boot when Windows starts. A Windows Scheduled Task running `wsl.exe -d <Distro> -u <runner-user> -- true` at log-on keeps the runner actually available instead of sitting idle until someone happens to open a WSL window.
- GHCR image push (on `push` to `master` only) needs repo Settings → Actions → Workflow permissions = "Read and write permissions" — the workflow's own `permissions:` block can only narrow that ceiling, never raise it.

**If a run stays Queued:** the WSL distro — and with it the runner's systemd service — isn't up. Check `sudo ./svc.sh status` from `~/actions-runner` inside WSL; `journalctl -u 'actions.runner.*' -f` and `~/actions-runner/_diag/*.log` explain why a job isn't being picked up.

## Jobs (path-filtered)

The `changes` job (`dorny/paths-filter`) decides which of `identity`, `portfolio`, `marketdata`, `strategy` (removed by spec-01), `reporting`, `gateway`, `contracts`, `servicedefaults`, `testinglib`, `web` actually need to run, based on changed paths. `contracts/**`, `Skarbiec.ServiceDefaults/**` and `Skarbiec.Testing/**` fan out to every service job, since every service references all three. Each service job: restore → build (warnings as errors, via `Directory.Build.props`) → test (Testcontainers — needs the Docker daemon the runner already has). `web` runs `npm ci && npm test` only when `web/package.json` exists and only on `web/**` changes.

These are separate named jobs rather than a `strategy.matrix` on purpose: a job-level `if:` can't see the `matrix` context, so `needs.changes.outputs[matrix.service]` would be invalid and GitHub would reject the whole workflow.

**Target state (spec-00):** triggers stay on `master` only (the old `praca_*` trigger hack is removed); two new jobs, `format` (`dotnet format --verify-no-changes`) and `openapi-client` (`gen:api` from build-time OpenAPI files + `git diff --exit-code`); the `web` job gains `typecheck`, `lint`, `build` and `format:check` alongside `test`; the `strategy` job disappears with the service (spec-01).

## Dependabot

`.github/dependabot.yml` — MassTransit `semver-major` updates (v9+) are excluded from what to merge, per ADR-012: v8 is deliberately pinned (OSS), v9 is commercially licensed. **Close those PRs, don't merge them.** Target state (spec-00): `open-pull-requests-limit: 3`, and the Strategy docker entry is removed alongside the service.
