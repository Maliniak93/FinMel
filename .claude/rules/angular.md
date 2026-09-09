---
paths:
  - "web/**"
---

# Angular conventions (Angular 22)

Verified against angular.dev, 2026-09. Workspace tour and generator details: `web/README.md`.

## Components

- Standalone components only — no NgModules.
- Zoneless change detection and `OnPush` are the v22 defaults: never opt out (no `zone.js`, no `provideZoneChangeDetection`).
- Signals first: `signal()`/`computed()` for state, `input()`/`output()`/`model()` instead of decorators, `effect()` only for real side effects — never to derive state.
- `inject()` instead of constructor injection.
- Native control flow `@if` / `@for` (always with `track`) / `@switch` / `@defer` — never `*ngIf`/`*ngFor`.

## UI kit

Angular Material + CDK (ADR-010, decided). Use Material components and the CDK for overlays, a11y and layout primitives; do not pull in a second component library.

## Forms

Reactive Forms (typed, `FormBuilder.nonNullable`), with errors rendered in `mat-error`. **Not Signal Forms**: `[formField]` needs a control implementing the Signal Forms control interfaces, which Material's form-field components do not — revisit when Material ships that support.

## Data access (ADR-013)

- Every call goes through the Gateway using the generated hey-api **fetch** clients in `web/src/app/api/<service>/`, configured once in `core/api-clients.ts` (`client.setConfig({ baseUrl })` — the generated clients ship with `baseUrl: false` on purpose, so an unconfigured client fails loudly instead of silently hitting a service's own port).
- Wrap those SDK functions in `resource()` for reads. Never `HttpClient`, never `httpResource()` — both would bypass the generated client and its interceptors.
- Auth: the access token stays in memory, the refresh token in an httpOnly cookie (ADR-005) — never `localStorage`. Token attach and refresh live in `core/auth/auth-interceptors.ts` as hey-api `client.interceptors`, not Angular HTTP interceptors.

## Structure & routing

- Feature folders mirroring backend slices: `src/app/features/<feature>/`; shared UI in `src/app/shared/`; cross-cutting singletons in `src/app/core/`; the shell in `src/app/layout/`.
- Lazy routes with `loadComponent`/`loadChildren`; guards and interceptors as functions, never classes.
- Route params reach components through `withComponentInputBinding()` + `input()` — do not inject `ActivatedRoute` for a plain param.

## Domain rules in the UI

- The server does all money math (`decimal`); the client only formats — `Intl.NumberFormat('pl-PL', { style: 'currency', currency: 'PLN' })` via `shared/format-money.ts`. No floating-point arithmetic on amounts.
- A price older than 7 days is stale: show the marker with its date and source.
- Manual-valuation assets: remind the user to refresh when `ManualValueDate` is old.
- Allocation, rebalancing and goal views always carry the "information, not investment advice" disclaimer.

## Gotchas

- Do **not** put `MatDialogModule` or `MatSnackBarModule` in a component's `imports` when the component only injects `MatDialog`/`MatSnackBar` — the module's own providers shadow the TestBed mock overrides and the spec silently tests the real thing.
- `resource().value()` **throws** while the resource is in an error state. Gate every read on `hasValue()`, outside the main `@if (error())` / `@else if` chain.
- `DateOnly` arrives as `"YYYY-MM-DD"`: convert through the local-midnight helpers, never `new Date(string)` (which parses as UTC and shifts the day).
- Backend enums arrive as **ints**, not names — map them to labels through a shared label map, and use `provideNativeDateAdapter()` for Material datepickers.

## After an API change

`npm run gen:api`, then `npm run typecheck`, then read the diff of `src/app/api/` and keep only the real schema delta — the generator occasionally rewrites every import by dropping the `.js` extension. Generated code is committed and excluded from lint.

## Verification

`npm run typecheck` · `npm run lint` · `npm run build` · `npm test` (Vitest) · `npm run format:check`. `npm run smoke:api` needs the local stack running.

## When unsure

Angular 22 is newer than training data — check context7 (`/websites/angular_dev`) before writing an API from memory.
