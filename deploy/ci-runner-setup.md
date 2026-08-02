# GitHub Actions self-hosted runner — WSL2 setup (T0.17)

`.github/workflows/ci.yml` targets `runs-on: self-hosted`, not GitHub-hosted `ubuntu-latest`.
The runner lives in a WSL2 Ubuntu distro on the dev machine, under a dedicated Linux user set up
for this purpose. A single runner instance is enough for now — matrix jobs (up to 6 services +
3 libraries + web) queue and run one at a time on it rather than in parallel; that's a deliberate
tradeoff for a solo project, not a bug.

Repo: `https://github.com/Maliniak93/FinMel` (private).

## 1. Prerequisites checklist (run as the dedicated runner user, inside WSL)

- [X] WSL2 distro has systemd enabled — confirm `/etc/wsl.conf` contains:
  ```ini
  [boot]
  systemd=true
  ```
- [X] Docker Desktop → Settings → Resources → **WSL Integration** → toggled ON for this distro.
- [X] The runner user is in the `docker` group:
  ```bash
  groups $(whoami)   # must list "docker"
  ```

  If not: `sudo usermod -aG docker $(whoami)`, then fully close every shell for that user in this
  distro (or `wsl.exe --terminate <Distro>` from Windows) and reopen — group membership only
  takes effect on a fresh login session.
- [X] `curl`, `tar`, `git` present (default on Ubuntu): `command -v curl tar git`.
- [ ] Outbound HTTPS reachable to: `github.com`, `api.github.com`, `*.actions.githubusercontent.com`,
  `objects.githubusercontent.com`, `ghcr.io`, `nuget.org`, `mcr.microsoft.com`, and the .NET
  install CDN (`dot.net` / `*.blob.core.windows.net`). No inbound ports needed — the runner only
  makes outbound long-poll connections to GitHub.
- [X] Free disk space: budget 15–20 GB (Docker layers for 6 service images, NuGet cache, the
  runner's own `_work` checkout). Run `docker system prune` occasionally.

## 2. Download and extract the runner

```bash
mkdir -p ~/actions-runner && cd ~/actions-runner

RUNNER_VERSION=2.336.0   # check https://github.com/actions/runner/releases/latest for a newer one
curl -fsSL -o actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz \
  https://github.com/actions/runner/releases/download/v${RUNNER_VERSION}/actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz

# Copy the exact sha256 shown on the release page (don't trust a pasted value blindly):
# https://github.com/actions/runner/releases/tag/v${RUNNER_VERSION}
echo "<SHA256_FROM_RELEASE_PAGE>  actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz" | sha256sum -c

tar xzf actions-runner-linux-x64-${RUNNER_VERSION}.tar.gz
```

## 3. Get a registration token and register

1. Open `https://github.com/Maliniak93/FinMel/settings/actions/runners/new` (logged in as a repo
   admin).
2. OS: **Linux**, Architecture: **x64**. GitHub shows a `./config.sh --url ... --token ...`
   command with a short-lived token (~1 hour) already filled in — copy it.
3. Run it as the dedicated runner user — **not root**, `config.sh` refuses to run as root by
   default:
   ```bash
   cd ~/actions-runner
   ./config.sh --url https://github.com/Maliniak93/FinMel --token <PASTE_TOKEN_HERE>
   ```
4. Prompts:
   - Runner name: anything recognizable, e.g. `skarbiec-wsl`.
   - Runner labels: leave the default — the workflow uses bare `runs-on: self-hosted`, no custom
     label is required.
   - Work folder: default `_work` is fine.

This writes `.runner` and `.credentials*` files into `~/actions-runner` — this runner's identity
and auth. Keep them private; they live outside the git repo and should never be committed.

## 4. Install as a systemd service

```bash
cd ~/actions-runner
sudo ./svc.sh install
sudo ./svc.sh start
sudo ./svc.sh status
```

`svc.sh install` also `systemctl enable`s the unit, so it starts automatically whenever this WSL
distro's systemd starts. WSL2 itself, though, only boots a distro when something invokes it —
Windows doesn't auto-start WSL on its own.

**To make the runner actually available whenever the machine is on** (recommended — otherwise CI
runs just sit queued until a WSL window happens to be open): create a Windows Scheduled Task that
runs at log-on, Action: `wsl.exe`, Arguments: `-d <Distro> -u <runner-user> -- true`. That alone
boots the distro (and therefore systemd and the runner service) in the background, no visible
window needed.

## 5. Verify everything works

- [ ] GitHub side: `https://github.com/Maliniak93/FinMel/settings/actions/runners` lists the
  runner with a green **Idle** dot.
- [ ] Local service: `sudo ./svc.sh status` (from `~/actions-runner`) reports active/running.
- [ ] Docker access as the runner user specifically:
  ```bash
  docker run --rm hello-world
  ```

  Must succeed without `sudo`. A permission error here means the `docker` group membership from
  step 1 didn't take — recheck `groups`, fully restart the WSL session for that user.
- [ ] End-to-end, path filtering — push a commit or open a PR touching only something under
  `services/Identity/**` (e.g. a comment tweak) and watch the **Actions** tab: the `identity` job
  under `build-test` should go queued → in progress → success, its log showing it ran on the
  self-hosted runner (not `Requested labels: ubuntu-latest`). Confirm `gateway` and `web` did
  **not** run.
- [ ] Same again touching only `contracts/**` — confirm all five service jobs (`identity`,
  `portfolio`, `marketdata`, `strategy`, `reporting`) run, `gateway` does not.
- [ ] Confirm a Testcontainers-based test actually passes inside that CI run (not just "build
  succeeded") — check the job log for the `dotnet test` step's pass count.
- [ ] Troubleshooting logs: `journalctl -u 'actions.runner.*' -f` (service logs) and
  `~/actions-runner/_diag/*.log` (runner's own detailed diagnostics) if a job doesn't pick up.

## 6. One repo setting this depends on

GHCR image push only runs on `push` to `master` (never on `pull_request`), using the workflow's own
`permissions: packages: write`. For that push step to actually succeed, check
`https://github.com/Maliniak93/FinMel/settings/actions` → **Workflow permissions** is set to
"Read and write permissions" — the per-workflow `permissions:` block can only narrow that
repo-level ceiling, never raise it.

## 7. Removing or rotating the runner later

```bash
cd ~/actions-runner
sudo ./svc.sh stop
sudo ./svc.sh uninstall
./config.sh remove --token <REMOVAL_TOKEN_FROM_SAME_GITHUB_PAGE>
```
