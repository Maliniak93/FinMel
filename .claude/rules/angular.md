---
paths:
  - "web/**"
---

# Angular conventions (Angular 22)

Verified against angular.dev, 2026-07. Items marked *(default — confirm in Phase 0)* are opinionated choices not backed by an ADR yet.

## Components

- Standalone components only — no NgModules.
- Zoneless change detection and `OnPush` are the framework defaults for new apps in v22 — never opt out (no `zone.js`, no `Eager` strategy, no `provideZoneChangeDetection`).
- Signals-first: `signal()`/`computed()` for state, `input()`/`output()`/`model()` instead of decorators, `effect()` sparingly (side effects only, never state derivation).
- `inject()` instead of constructor injection.
- Native control flow `@if`/`@for` (with `track`)/`@switch`/`@defer` — never `*ngIf`/`*ngFor`.

## Data access (ADR-013)

- All HTTP goes through the Gateway via the TS client generated from each service's OpenAPI doc — never hand-written service URLs or raw `HttpClient` calls to business endpoints.
- Async data as signals: `httpResource()`/`resource()` (stable in v22) wrapping the generated client where reactivity is needed; handle `loading`/`error` states in the template.
- Auth: access token in memory, refresh token in httpOnly cookie (ADR-005) — never store tokens in localStorage.

## Forms

- Signal Forms (`@angular/forms/signals`, stable in v22): `form()` + `[formField]` — type-safe, no ControlValueAccessor boilerplate. *(default — confirm in Phase 0; fallback: typed reactive forms)*

## Structure & routing

- Feature folders mirroring backend slices (`web/src/app/features/<feature>/`); shared UI in `shared/`.
- Lazy routes with `loadComponent`/`loadChildren`; guards and interceptors as functions, not classes.
- UI kit: ADR-010 still open (Material vs PrimeNG spike) — do not commit to either in generated code until decided.

## Domain rules in the UI

- Money: server computes all monetary math (`decimal`); the client only formats — `Intl.NumberFormat('pl-PL', { style: 'currency', currency: 'PLN' })`. No floating-point arithmetic on amounts.
- Prices older than 7 days are `stale` — show the marker with date and source.
- Rebalancing/strategy views always carry the "information, not investment advice" disclaimer.
- Manual-valuation assets: remind about refresh when `ManualValueDate` is old.

## Testing

- Vitest (default runner for new Angular projects since v22) for unit tests *(default — confirm in Phase 0)*; Playwright e2e through the Gateway.

## When unsure

Angular 22 is newer than training data — verify APIs via context7 (`/websites/angular_dev`) before writing code from memory.
