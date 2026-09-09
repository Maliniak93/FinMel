# Phase 4 — Convenience and trust

**Duration:** 3–4 weeks.
**Goal:** the app earns daily-driver trust: e-mail notifications for the things that matter, CSV import/export so data isn't hostage, an annual report, and hardening (backups, rate limits, security review).
**Deliverable:** notifications flowing, XTB CSV imported successfully, annual report renders, hardening checklist closed.
**Sources:** roadmap Phase 4, E3 [S]/[C] (import/export), E5 [C] (TWR), E6/E7 [C] (notifications), E8.

## Task index

| ID | Task | Size | Depends on |
|---|---|---|---|
| T4.1 | Notifications: service skeleton + e-mail sending | L | Phase 3 |
| T4.2 | Contracts: notification-driving events from Strategy | M | Phase 3 |
| T4.3 | Notifications: drift + emergency-fund e-mails | M | T4.1, T4.2 |
| T4.4 | Import CSV: parsers (XTB + generic) | L | Phase 3 |
| T4.5 | Import CSV: preview, dedup, per-row errors, commit | L | T4.4 |
| T4.6 | Export (CSV/JSON) | M | Phase 3 |
| T4.7 | Annual report: simplified TWR, contributions vs growth | L | Phase 3 |
| T4.8 | Hardening: backup coverage + restore test (local now; VPS parts optional) | S | T4.1 |
| T4.9 | Hardening: rate limiting review + security review | M | all features |
| T4.10 | Phase exit: local smoke (+ optional VPS deploy) | S | T4.1–T4.9 |

---

### T4.1 Notifications: service skeleton + e-mail sending

- [ ] **Status:** todo · **Size:** L
- **Depends on:** Phase 3
- **Refs:** roadmap Phase 4, ADR-001 (Notifications planned for Phase 4), `02-architecture.md` §services

**Goal:** the sixth service exists: consumes events, sends e-mails through a provider, records what it sent.

**Scope:**
- `services/Notifications/` on the standard skeleton (T0.13 pattern): ServiceDefaults, `notifications_db` + own DB user, health, OTel, MassTransit consumer host (it may need no public HTTP API beyond health — decide; gateway route only if a UI need appears).
- E-mail sending: SMTP relay or transactional provider (decision at implementation time — criteria: free tier, EU region, deliverability; record as a note in this task when decided); abstraction `IEmailSender` so the provider is swappable; templates as simple text/HTML files (no template-engine dependency unless needed).
- `SentNotification` record: `UserId`, type, dedup key, sent-at — the basis for T4.3's no-spam rule.
- Recipient e-mail: consume `UserRegistered` to maintain a local user-email copy (read model — the CQRS pattern again), so Notifications never synchronously asks Identity at send time.
- Aspire + compose + CI wiring (path filters extended).

**Acceptance criteria:**
- [ ] Service runs locally and in production; consumes a test event and sends a real e-mail to a test inbox.
- [ ] `UserRegistered` builds the local e-mail read model (integration test: register → row appears).
- [ ] Provider credentials only in user-secrets/`.env`; failure to send → retry per messaging policy, then error queue (no silent loss).

---

### T4.2 Contracts: notification-driving events from Strategy

- [ ] **Status:** todo · **Size:** M
- **Depends on:** Phase 3
- **Refs:** E6 [C], E7 [C], ADR-012 (outbox), contracts versioning rules (T0.2)

**Goal:** Strategy detects and publishes the facts Notifications needs: allocation drift beyond band, emergency fund below threshold.

**Scope:**
- New records in `Skarbiec.Contracts` (additive): `AllocationDriftDetected` (UserId, scope, classes outside band with deviations, detected-at), `EmergencyFundBelowThreshold` (UserId, coverage %, threshold, detected-at).
- Detection needs a trigger — deviations are computed on demand today. Add a small daily Quartz job in Strategy (after the snapshot window, i.e. after MarketData sync + Reporting snapshots; document the schedule reasoning) that evaluates each user's allocation + emergency fund and publishes via the Strategy outbox **only on state change** (was-in-band → now-out; above → below threshold) — state tracked in a small `DetectionState` table to avoid re-publishing the same fact daily.
- Threshold for the fund (e.g. coverage < 100% or a user-set value — start fixed, document).

**Acceptance criteria:**
- [ ] Drift crossing the band publishes exactly one event; staying out of band the next day publishes nothing (state-change test).
- [ ] Returning in-band resets state (next drift publishes again).
- [ ] Events through the outbox in the job's transaction; contract deserialization tests.

---

### T4.3 Notifications: drift + emergency-fund e-mails

- [ ] **Status:** todo · **Size:** M
- **Depends on:** T4.1, T4.2
- **Refs:** E6 [C], E7 [C]

**Goal:** the two events become useful, non-spammy e-mails.

**Scope:**
- Consumers (inbox/dedup per T0.12) for both events → templated e-mails: what happened, current numbers, link into the app; informational tone, same no-advice stance as T3.3.
- No-spam guard on top of event-level dedup: at most one e-mail per notification type per user per day (checked against `SentNotification` dedup key) — belt and suspenders over T4.2's state-change logic.
- Unsubscribe/notification-preferences: minimal — a per-user opt-out flag (settings endpoint + UI checkbox can be a small add-on; if it slips, document that e-mails are on by default with opt-out coming — decide consciously).

**Acceptance criteria:**
- [ ] Each event type produces the right e-mail (integration test with a fake sender capturing output).
- [ ] Same event delivered twice → one e-mail (inbox); two distinct events same day same type → one e-mail (daily guard).
- [ ] E-mail renders correctly in a real inbox (manual check, screenshot in phase log).

---

### T4.4 Import CSV: parsers (XTB + generic)

- [ ] **Status:** todo · **Size:** L
- **Depends on:** Phase 3
- **Refs:** E3 [S], `01-vision-and-scope.md` §v1.0 (item 12)

**Goal:** XTB's export format and a documented generic format parse into transaction candidates.

**Scope:**
- In Portfolio (import creates transactions — the owner of that data): parsing layer separate from HTTP (pure, heavily testable): input stream → list of `TransactionCandidate` (asset match hint, type, quantity, price, fee, currency, date) + list of row errors.
- XTB parser: their cash-operations/closed-positions CSV as actually exported (obtain a real sample; fixtures anonymized); mind decimal comma, date formats, PL headers, type mapping (XTB operation types → our `TransactionType`).
- Generic format: define + document it in `skarbiec-plan/` or the app docs (columns: date, type, asset name/ticker, quantity, unit price, fee, currency) — the escape hatch for every other broker.
- Asset matching strategy: by ticker/instrument when resolvable, else by asset name within the target portfolio, else "unmatched" (user resolves in preview, T4.5).

**Acceptance criteria:**
- [ ] Fixture tests: real-shaped XTB file parses; each documented XTB operation type maps correctly or lands as a row error (never silently skipped).
- [ ] Generic format round-trips (export from T4.6 re-imports cleanly — the formats should align).
- [ ] Malformed rows produce per-row errors with line numbers; good rows still parse (no all-or-nothing at parse stage).

---

### T4.5 Import CSV: preview, dedup, per-row errors, commit

- [ ] **Status:** todo · **Size:** L
- **Depends on:** T4.4
- **Refs:** E3 [S] AC (preview before saving, deduplication, per-row error report)

**Goal:** the full import flow: upload → preview with errors and duplicate flags → user confirms → transactions created atomically.

**Scope:**
- Endpoints: `Features/Import/PreviewImport` (multipart upload + target portfolio + format → parsed candidates with statuses: ok / duplicate / unmatched-asset / error) and `CommitImport` (selected candidate set → create transactions + recompute positions, one DB transaction).
- Dedup: candidate matches an existing transaction on (asset, date, type, quantity, unit price) → flagged duplicate, unchecked by default (user can force).
- Preview state between the two calls: stateless (client resubmits candidates) or a short-lived server-side import session — decide at implementation, document; stateless is simpler and restart-safe.
- Angular: import wizard (upload → preview table with per-row status/errors, checkboxes, unmatched-asset resolver dropdown → confirm → summary); file size limit + friendly errors.
- Commit re-validates every row server-side (never trust the previewed client payload — invariants from T1.3 apply, e.g. oversell).

**Acceptance criteria:**
- [ ] Import of a real XTB file end-to-end in the UI: preview shows errors/dupes, commit creates exactly the selected rows, positions recompute.
- [ ] Re-importing the same file → everything flagged duplicate, default selection empty.
- [ ] A row violating invariants at commit (e.g. oversell) fails that row's import with a clear error and does not corrupt the rest (define + test atomicity policy: all-or-nothing per commit — document choice).

---

### T4.6 Export: all data as CSV/JSON [C]

- [ ] **Status:** todo · **Size:** M
- **Depends on:** Phase 3
- **Refs:** E3 [C]

**Goal:** users can take their data out — portfolios, assets, transactions (and strategy config) as CSV and JSON.

**Scope:**
- Portfolio: `Features/ExportData` — JSON (full fidelity, one document) and CSV (transactions in the T4.4 generic format; assets/portfolios as separate CSVs in a zip); tenancy-filtered, streamed (no giant in-memory buffers).
- Strategy config export (allocations, fund, goals) as JSON — separate endpoint in Strategy (each service exports its own data; no cross-DB reach — ADR-003). UI stitches the download links on a settings page.
- Angular: "Export my data" section (settings): buttons per format.

**Acceptance criteria:**
- [ ] Transactions CSV re-imports through the generic importer with zero errors and full dedup flags (round-trip test).
- [ ] JSON contains all user-owned entities of the exporting service; user B's export contains none of A's data (isolation test).
- [ ] 5k-transaction export streams without memory spikes (rough check).

---

### T4.7 Reporting: annual report — simplified TWR, contributions vs growth [C]

- [ ] **Status:** todo · **Size:** L
- **Depends on:** Phase 3
- **Refs:** E5 [C] (simplified yearly TWR), roadmap Phase 4

**Goal:** a yearly report: portfolio rate of return (simplified TWR) and the split of net-worth change into contributions vs market growth.

**Scope:**
- In Reporting (it owns snapshots — the data source): `Features/GetAnnualReport` (year param): 
  - **Simplified TWR**: sub-period returns between external cash flows using daily snapshots and flow data; flows (deposits/withdrawals/buys funded externally) fetched from Portfolio via REST — define precisely what counts as an external flow (deposits/withdrawals yes; dividends/interest no — reinvestment; document the method next to the code, it's a learning artifact).
  - **Contributions vs growth**: Δ net worth over the year = net contributions + market growth (residual).
- Pure calculation functions, table-driven tests with hand-checked scenarios (flat year, single mid-year deposit, withdrawal, negative return).
- Angular: report page (year picker, headline TWR %, contributions-vs-growth bar, per-portfolio table); "simplified — not audit-grade" caveat.

**Acceptance criteria:**
- [ ] TWR verified against hand-computed cases including a mid-period flow (the case naive return gets wrong).
- [ ] Contributions + growth exactly reconcile to the net-worth delta (assert in tests).
- [ ] Report renders in the UI for a year of real snapshot data.

---

### T4.8 Hardening: backup coverage + restore test (local now; VPS parts optional)

- [ ] **Status:** todo · **Size:** S
- **Depends on:** T4.1
- **Refs:** E8 [M], roadmap Phase 4 (backups of all databases)

**Goal:** the backup net covers every database that now exists, and restore is re-proven.

**Scope:**
- **Local (required now):** extend the T1.14 backup script's coverage check: all six databases (`identity`, `portfolio`, `marketdata`, `strategy`, `reporting`, `notifications`) — make the script enumerate databases dynamically so a new one can't be silently missed (that's the actual hardening); develop/test this against the local Aspire-managed Postgres via plain `pg_dump`/`pg_restore`. Restore test: restore `portfolio_db` + `reporting_db` to a local scratch database and cross-check a dashboard number.
- **VPS (optional — deferred, needs T1.14/T0.18):** verify off-VPS copies still shipping; check retention actually prunes.

**Acceptance criteria:**
- [ ] A database added to PG without backup coverage causes a visible script warning/failure (test by faking one, locally).
- [ ] Restore test done and logged against a local scratch database.
- [ ] *(optional, deferred)* Off-VPS copies verified shipping; retention pruning confirmed — once T1.14/T0.18 exist.

---

### T4.9 Hardening: rate limiting review + security review

- [ ] **Status:** todo · **Size:** M
- **Depends on:** all Phase 4 features
- **Refs:** E8, roadmap Phase 4, ADR-013

**Goal:** a deliberate pass over the security posture before calling the system trustworthy.

**Scope:**
- Rate limiting review at the gateway: per-route policies sane for real usage (auth endpoints stricter; import upload size/frequency limited; manual sync trigger capped) — verify with a quick load poke, tune.
- Security review checklist: JWT config (alg, key size, expiry), cookie flags, CORS tightness, headers (HSTS, no sniff…), secrets not in repo/images, PG users' privileges still minimal, RabbitMQ/Grafana not publicly exposed, dependency audit (`dotnet list package --vulnerable`, `npm audit`).
- Run the `/security-review` skill on the branch as an automated complement; triage findings — fix criticals now, backlog the rest with severity.
- Document outcomes in `deploy/security-notes.md` (what was checked, when, findings, decisions).

**Acceptance criteria:**
- [ ] Checklist executed and committed; criticals fixed, rest backlogged with severity labels.
- [ ] 429s verified on auth + import + sync-trigger routes.
- [ ] Vulnerability scans clean or consciously waived (documented).

---

### T4.10 Phase exit: local smoke (+ optional VPS deploy)

- [ ] **Status:** todo · **Size:** S
- **Depends on:** T4.1–T4.9
- **Refs:** rule "every phase ends with a deployment" (currently relaxed to "every phase ends with a local smoke", VPS optional — see `zadania/README.md`)

**Goal:** Phase 4 feature set demonstrably works end-to-end locally; the VPS/production half stays optional until T0.18 is picked up.

**Scope:**
- **Local (required now):** smoke under Aspire: force a drift (edit an allocation band) → detection job → e-mail arrives at the local test inbox (per T4.1's e-mail-provider choice); import a small CSV; export + re-import round-trip; annual report renders.
- **VPS / production (optional — deferred, needs T0.18):** deploy (Notifications joins the stack — memory budget recheck on the VPS); repeat the smoke in production.

**Acceptance criteria:**
- [ ] Local smoke green including a real notification e-mail landing in the local test inbox.
- [ ] *(optional, deferred)* Production smoke green; VPS memory within limits with 7 .NET containers + PG + RabbitMQ + observability — once T0.18 lands.

---

## Exit checklist (Phase 4 done when…)

- [ ] All tasks done ([C] items — T4.6, T4.7 — consciously confirmed or moved with a note).
- [ ] Local smoke green; notification e-mail received in the local test inbox.
- [ ] Backup coverage script dynamic; local restore test done this phase (T4.8).
- [ ] Security review documented; criticals fixed.
- [ ] *(optional, deferred)* Deployed to VPS; production smoke green; off-VPS backup copies verified — pick up together with T0.18.
