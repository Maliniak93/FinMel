# e2e

Playwright tests that click through the whole app on an isolated compose stack with fixed market data. Rules: `.claude/rules/e2e.md`; decision: ADR-034.

```bash
node scripts/e2e.mjs run                    # build, start, test, tear down
node scripts/e2e.mjs run --grep "market data"
node scripts/e2e.mjs run --headed           # watch Chromium, no retries
node scripts/e2e.mjs run --ui               # Playwright UI, no retries
node scripts/e2e.mjs run --keep             # leave the stack up afterwards
node scripts/e2e.mjs up | down | status | report
node scripts/e2e.mjs run --detach           # then: node scripts/e2e.mjs run --await
node scripts/e2e.mjs check                  # exit 0 only for a green whole-suite run of this exact tree (the ship gate of an e2e part)
```

Docker must be running. The first run installs `e2e/` dependencies and Chromium, then builds six service images and the stub from the working tree (uncommitted changes included), so a run costs a few minutes. The user's `skarbiec-local` stack is not touched.

## Stack

| What                                     | Where                                     |
| ---------------------------------------- | ----------------------------------------- |
| App (web, proxies `/api` to the Gateway) | http://localhost:18080                    |
| Market stub and its request log          | http://localhost:18081, `GET /__requests` |
| Aspire dashboard                         | http://localhost:18898                    |
| Postgres, RabbitMQ                       | not published, tmpfs                      |

Project `skarbiec-e2e`, images `skarbiec-e2e/<svc>`, override `deploy/compose/compose.e2e.yaml`. The Gateway permits 10 000 standard and 1 000 auth requests per 10 s; MarketData runs its price and FX syncs every 15 s.

## Result and failures

`run` writes `.git/e2e-result.json` (`ok`, `class` = `pass` / `fail` / `environment`, `total`, `grep`, `failed[]` with the test, failing step, error line and trace path, `flaky[]`, `reportPath`, working-tree `fingerprint`) and exits 0, 1 or 3. `--await` exits 4 while a detached run is still going.

After a failure:

- `node scripts/e2e.mjs report` opens the HTML report (`e2e/playwright-report/`).
- Open a trace: `cd e2e && npx playwright show-trace test-results/<test>/trace.zip` (the path is in `failed[].trace`). The video is `test-results/<test>/video.webm`, the screenshot beside it.
- `e2e/test-results/stack.log` holds the logs of every container.

Agent runs retry a failed test twice; a test that passes on retry is reported as flaky, not failed.

## Data set

Fixed prices served by the stub; see [market-data/README.md](market-data/README.md).

| Item                                 | Value                          |
| ------------------------------------ | ------------------------------ |
| USD/PLN, EUR/PLN, GBP/PLN, CHF/PLN   | 4.0000, 4.2500, 5.0000, 4.5000 |
| CDR.WA, VWCE.DE                      | 100.00 PLN, 100.00 EUR         |
| PKO.WA (found by search, not seeded) | 50.00 PLN                      |
| XAU, XAG                             | 300.00 USD/g, 3.00 USD/g       |
| Bond series OTS0127                  | first-period rate 2.00 %       |
| NODATA.WA (any unlisted ticker)      | no data                        |

## Layout

```
playwright.config.ts   baseURL, retries, trace/video/screenshot, reporters
tests/                 fixtures.ts (user, Polish UI, sign-in helpers), smoke.spec.ts, market-data.spec.ts
stub/                  dependency-free Node server + Dockerfile
market-data/           data.json, mf-bonds.xls, README.md
```
