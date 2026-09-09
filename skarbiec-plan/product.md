# Product

> Working name: **Skarbiec**

## Vision

A multi-user web application for managing personal wealth. The user enters assets and transactions manually; the system automatically fetches currency rates and price quotes (stocks, ETFs, precious metals, crypto), values the portfolio, tracks net-worth history, and supports decision-making: target allocation, rebalancing, emergency fund, savings goals.

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

## Features

### Delivered

- Registration / login; 15-minute JWT + rotated httpOnly refresh cookie; per-user data isolation.
- Portfolios: create, edit, archive.
- Assets in three valuation modes: market (ticker-priced), manual, currency-valued.
- Transactions: buy, sell, deposit, withdrawal, dividend, interest — asset quantity derived from transaction history.
- Automatic FX rates (NBP) and quotes (Stooq, CoinGecko) — daily job.
- Ticker verification before a market asset is created, so a typo doesn't silently create a dead instrument (ADR-018).
- Portfolio valuation in PLN, net-worth dashboard, value history.
- Dark theme.

### Next

Bring-up specs `spec-00`…`spec-06` close the gap between today's code and the target architecture (`architecture.md`) — event-carried positions, a currency catalog with its own sync job, squashed migrations — with no new user-facing feature. After that, v1.0's insight features land as ordinary specs against the new model:

- Target allocation per portfolio or total wealth, with deviation detection.
- Rebalancing suggestions (asset-class buy/sell amounts) and a "where should I deposit X" contribution-planning mode.
- Emergency fund coverage against designated assets.
- Savings goals with progress and required monthly contribution.

### Later

See `ideas.md` for the full, tiered list: profit/loss per asset, CSV import/export, an annual report (simplified TWR), notifications, Playwright e2e coverage, VPS deployment, liabilities, PIT-38/FIFO, household sharing, PWA, open banking.

## MVP success criteria (met)

- Entering the entire wealth takes under 30 minutes.
- Daily valuation updates with no user intervention.
- Dashboard responds in under 1 second.
- A second user sees nothing of another user's data (isolation tests).

## Out of scope (deliberately)

- Executing trades — the app only records and suggests, never trades.
- Investment advice — every suggestion is phrased as information, not a recommendation.
- Intraday data — daily (end-of-day) prices are enough.
