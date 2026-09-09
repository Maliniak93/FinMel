# 01 — Vision and scope

> Working name: **Skarbiec** (subject to change)

## Vision

A multi-user web application for managing personal wealth. The user enters assets and transactions manually, and the system automatically fetches currency rates and price quotes (stocks, ETFs, precious metals, crypto), values the portfolio, tracks net-worth history and supports decision-making: target allocation, rebalancing, emergency fund, savings goals.

## The problem we solve

- Wealth scattered across many places (bank accounts, IKE/IKZE, broker, crypto, real estate, cash) — no single net-worth view.
- Manual spreadsheets don't update prices and don't compute rebalancing.
- No systematic tracking of strategy execution (allocation drift, emergency-fund status, goal progress).

## Users

| Persona | Needs |
|---|---|
| Owner (you) | full wealth picture, strategy, rebalancing |
| Other users (multi-user from the start) | own, isolated accounts and portfolios |
| (future) Household | shared portfolios, roles |

## Features — scope

### MVP (must have)
1. Registration / login, per-user data isolation.
2. Portfolios (e.g. "IKE", "Broker", "Crypto", "Real estate").
3. Assets of various classes: cash, term deposits, stocks/ETFs, bonds, crypto, precious metals, real estate, other (manual value).
4. Transactions: buy, sell, deposit, withdrawal, dividend, interest.
5. Automatic currency rates (NBP) and quotes (Stooq, CoinGecko) — daily job.
6. Portfolio valuation in PLN (base currency) + net-worth dashboard.
7. Value history (daily snapshots) + chart.

### Version 1.0
8. Target allocation per portfolio/total (asset classes, % + tolerance band).
9. Rebalancing: drift detection, "buy/sell for X" suggestions.
10. Emergency fund: monthly expenses × target number of months, designated assets, % coverage.
11. Savings goals (amount, deadline, progress, required monthly contribution).
12. Transaction import from CSV (XTB, generic format).

### Later (don't plan in detail now)
- Notifications (allocation drift, emergency-fund drop, goal milestone).
- Tax report (PIT-38 helper), FIFO.
- Sharing portfolios within a household.
- Open banking (automatic account balances) — high cost/regulations, deliberately postponed.
- PWA / mobile.

## Out of scope (deliberately)
- Executing trades (the app only records and suggests — it never trades).
- Investment advice — messages are phrased as information, not recommendations.
- Intraday data — daily (EOD) prices are enough.

## MVP success criteria
- Entering the entire wealth takes < 30 min.
- Daily valuation updates with no user intervention.
- Dashboard responds in < 1 s.
- A second user sees nothing of others' data (isolation tests).
