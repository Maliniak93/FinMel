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

- Colours, type and shape come from the Classical tokens in `web/src/skarbiec-tokens.css`, loaded after `styles.scss` (`mat.theme()`) in `angular.json`, so its `--mat-sys-*` and `--sk-*` win. Use the tokens, never literal colours.
- `<html data-theme="light|dark">` always holds the resolved theme: `ThemeService` (and the pre-paint script in `index.html`) resolve `system` through `matchMedia`; no inline `color-scheme`.
- Fonts (Cormorant Garamond, Lora, Material Symbols Outlined) are self-hosted from `@fontsource/*` woff2 files declared in `styles.scss`; no Google Fonts, Roboto or Material Icons. Icons are Material Symbols ligatures in `<mat-icon>` (default font set `material-symbols-outlined`); every ligature name must exist in the `material-symbols` package (`icons.spec.ts`).
- Figures are tabular (`body { font-variant-numeric: tabular-nums lining-nums }`, `.sk-num`).

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

## Languages (i18n)

The UI runs on **Transloco** (`@jsverse/transloco`), English by default, Polish as a runtime choice (persisted per browser in `skarbiec-lang`). Setup lives in `src/app/core/i18n/`; translations in `src/i18n/en.json` and `src/i18n/pl.json` (the only place Polish text lives). How to add a key: `web/README.md` → Languages.

- **No hardcoded user-facing text** in templates or TS: every string is a key in both JSON files. `en.json` and `pl.json` always hold the same keys (`core/i18n/translations.spec.ts` enforces it).
- **Key naming:** nested by area — `shell.nav.dashboard`, `settings.syncNow`, `enums.assetClass.cash`; shared words under `common.*`, frontend error fallbacks under `errors.*`. Parameters use `{{ name }}` interpolation, never string concatenation.
- **Plurals:** there is no ICU/messageformat plugin. Default: phrase a count as "label: N" (`zsynchronizowano: 3`). When a caption must read as a noun phrase ("2 accounts"), use a `<key>.{one,few,many,other}` group, pick the key with `new Intl.PluralRules(activeLocale()).select(n)` and pipe it through transloco with `{{ count }}` interpolation (`wealth.accountsCount.*` in `wealth-layout.ts`); give every category the locale needs in both JSON files.
- **Templates translate only through the `transloco` pipe or directive**, so a language switch re-renders without a reload (the app is zoneless). `translate()` from `@jsverse/transloco` is for one-shot text built at the moment of an action (a snackbar, a fallback error message, a dialog opened after the switch).
- **Label maps return keys, not text** (`assetClassLabel`, `TRANSACTION_TYPES`, `SUPPORTED_CURRENCIES`, …); the template pipes them. A label with parameters returns `{ key, params }` (`transferLabel`).
- **Formatting goes through the locale-aware helpers** in `shared/format.ts` — `formatMoney`, `formatQuantity`, `formatPercent`, `formatDate`, `formatMonth`, `formatDateTime` — never `DatePipe`, `CurrencyPipe` or a hardcoded `Intl` locale. Locale mapping: `en` → `en-US`, `pl` → `pl-PL`, for numbers, money, dates and the Material `DateAdapter`. `formatMoney` gives PLN as `zł` in Polish and every other currency by its ISO code (`420,00 EUR`), always grouped, with U+2212 as minus and no sign at zero; colours of groups and asset classes come from `shared/colors.ts` as `var(--sk-g-*)` / `var(--sk-c-*)` references (bind them through `style`, not an SVG attribute), and `app-money` renders the amount states. `formatQuantity` shows every quantity, weight and count with at most 2 decimals (half-up, grouped, no trailing zeros, `< 0,01` for a tiny non-zero value); display only — form fields and requests keep the exact value.
- **Templates are guarded:** `src/app/i18n-guard.spec.ts` scans every `src/app/**/*.html` and fails on a text node, a `placeholder`/`aria-label`/`matTooltip`/`title`/`label` attribute, or a capitalised string literal inside `{{ }}` that does not go through the `transloco` pipe. Bind those attributes (`[matTooltip]="'key' | transloco"`); its allowlist holds only non-translatable tokens (brand, currency codes), never an English word. TS-built strings are not scanned — cover them in the screen spec.
- **Backend text is shown as-is:** a ProblemDetails `detail` stays English; only the frontend's own fallbacks are translated.
- **Specs** that render translated text add `provideI18nTesting()` (`core/i18n/testing.ts`, English active). A spec that switches to Polish switches back to English in `afterEach` — specs share one worker.

## Domain rules in the UI

- The server does all money math (`decimal`); the client only formats, through `shared/format.ts` (see Languages). No floating-point arithmetic on amounts. The one exception is a display-only sum a spec asks for from stored inputs (the net interest in the deposit settlement and bank-interest dialogs, a deposit's final amount — `settlementAmounts` in `features/deposits/deposit-terms.ts` — a bond purchase's cost, count × price, shown and checked against the funding Cash balance in `bond-purchase-dialog`, a bond swap's cost and leftover, the previewed proceeds − count × swap price, in `swap-bond-dialog`, the account-cash hint of a PLN stock or ETF trade, quantity × price ± fee (a Dividend's net amount), in `transaction-form-dialog`, and the value-today gross and net totals over the rows with an estimate in `my-bonds`, the fill ratio of the contribution bar in `contribution-bar`, the value / target percent of a group's row, the sum of the group contributions, the Wydatki row (`expenses.costPln` minus that sum, as the server's cost already includes the contributions) and the bar widths scaled to the largest cascade amount in `month-card`, a group's class shares against its allocation, a cushion's missing amount and the sum of the unknown-group entries into "Bez grupy" in `dashboard`, the PLN equivalent per currency summed from the cash places' balances in `cash-card`, the segment shares in `split-bar`'s text alternative, the header, section and tab sums over the valued rows in `wealth-data`, in grosze, the running sum of the class allocation rows in `group-form-dialog` and of the share inputs in `planning`, in tenths of a percent), done in whole grosze; the server's values stay the truth.
- A price older than 7 days is stale: show the marker with its date and source.
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
