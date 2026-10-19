---
name: frontend-playbook
description: Angular 22 component, data-access, and forms conventions for the Skarbiec SPA.
user-invocable: false
---

# Frontend playbook
Angular 22, standalone components only, zoneless + OnPush (framework defaults — never opt out: no `zone.js`, no
`provideZoneChangeDetection`). Signals-first: `signal()`/`computed()`, `input()`/`output()`/`model()`, `inject()`
instead of constructor injection, native control flow (`@if`/`@for` with `track`/`@switch`/`@defer`) — never
`*ngIf`/`*ngFor`. UI kit is Angular Material + CDK (ADR-010).

## Structure
- Feature folders: `features/<name>/`, dialogs in their own subfolder (`features/<name>/<name>-form-dialog/`). Cross-feature UI in `shared/`. Auth/API wiring in `core/`.
- Lazy routes with `loadComponent`/`loadChildren`; guards/interceptors as functions, not classes.
- Route params via `withComponentInputBinding()` + `input.required<T>()` on the component — never `ActivatedRoute` snapshot digging.

## Data access
- The **only** way to call the backend: a generated hey-api fetch function from `web/src/app/api/<service>/`, wrapped in `resource()`. Never inject `HttpClient`, never call `httpResource()` directly against a URL, never call a service other than through the Gateway (ADR-013).
- Pattern (copy `features/assets/assets.ts`): `resource({ params: () => ({ id: this.id() }), loader: async ({ params, abortSignal }) => { const result = await getApiThingById({ path: { id: params.id }, signal: abortSignal }); if (result.error) throw new Error(readProblemDetails(result.error).detail ?? 'Failed to load.'); return result.data; } })`.
- Every template consuming a `resource()` handles all three states — loading (`isLoading()`), error (`error()`), empty (a success value with zero items) — never just the happy path.
- **`resource().value()` throws while the resource is in error state.** Never call it as the first branch of a template conditional; check `hasValue()` first, or branch on `isLoading()`/`error()` before touching `value()`.
- Auth token attachment and 401-refresh-retry are hey-api `client.interceptors.request`/`response` registered in `core/auth/auth-interceptors.ts` — extend those, never add an Angular `HttpInterceptorFn` for API calls.

## Forms
- Reactive Forms (`FormBuilder`/typed forms) + `mat-error` for validation messages. **Not** Signal Forms — Angular Material's form-field integration does not support them; do not introduce `@angular/forms/signals` here even though it is stable in v22.
- Datepickers need `provideNativeDateAdapter()` — in the component/route providers, and again in every spec's `TestBed.configureTestingModule` that renders one.

## Gotchas
- **`MatDialogModule`/`MatSnackBarModule` in a component's `imports`**, when that component only injects `MatDialog`/`MatSnackBar` (no `<mat-dialog-*>` markup of its own), shadows the TestBed provider override in specs. Only import them where the directives are actually used in the template.
- **Enums arrive over the wire as raw ints** (e.g. `AssetClass`) — never assume a string. Keep one label map per enum, next to where it's displayed.
- **`DateOnly` round-trips as a `"YYYY-MM-DD"` string** — never construct it with `new Date(isoString)` (parses as UTC midnight, can render as the previous day depending on the viewer's offset). Use explicit local-midnight construction/formatting helpers (`toDateOnly`/`fromDateOnly` in `shared/date-only.ts` — import them, never copy them), built from `getFullYear()`/`getMonth()`/`getDate()`, not string slicing of a `Date`'s ISO output.
- Money: the server computes every monetary value as `decimal` — the client only formats, via the locale-aware helpers in `shared/format.ts` (`formatMoney`, `formatQuantity`, `formatPercent`, `formatDate`, `formatDateTime`; `en` → `en-US`, `pl` → `pl-PL`). Never `DatePipe` or a hardcoded `Intl` locale, and never arithmetic on a formatted or floating-point amount client-side.

## Languages (i18n)
- Transloco, English by default, Polish at runtime; files in `src/i18n/{en,pl}.json`, setup in `core/i18n/`. Full rules: `.claude/rules/angular.md` → Languages.
- No hardcoded user-facing text: add the key to **both** JSON files (nested by area, e.g. `settings.syncNow`; shared words under `common.*`; `{{ param }}` interpolation, no plurals — "label: N").
- Templates translate only through the `transloco` pipe or directive; `translate()` only for one-shot text built at the moment of an action. Label maps return keys, templates pipe them.
- Specs rendering translated text add `provideI18nTesting()` (`core/i18n/testing.ts`); a spec that switches to Polish switches back to English in `afterEach`.

## API changes
1. `npm run gen:api` (`openapi-ts`) regenerates `src/app/api/<service>/` from each service's OpenAPI document. Once build-time OpenAPI files exist under `web/openapi/`, this reads those directly; until then it needs the Aspire stack running (`dotnet run --project Skarbiec.AppHost`) so it can fetch `/api/<service>/openapi/v1.json` through the Gateway.
2. `npm run typecheck`.
3. `git diff -- src/app/api`. The generator can drop unrelated `.js` import extensions repo-wide as a side effect of an unrelated version bump. If the diff is wider than the schema change you made, discard the regeneration and hand-apply only the real delta instead of committing the noise.

## Verification (all must pass)
`npm run typecheck` · `npm run lint` · `npm run build` · `npm test` · `npm run format:check` (prettier runs on edit via a hook; this just confirms it stayed clean). Keep bundle sizes inside the configured budgets — a budget warning is a build failure here.

> **Who runs them.** `scripts/verify.mjs` runs the web checks (typecheck, lint, build, test, format) in the implementer's verify run before it returns, when the changed files are in `web/`. While implementing, run only the spec you are driving green (`npm test -- --watch=false -t "<name>"`), plus the single `npm run typecheck` that follows a `gen:api` regeneration. Running the suites yourself only repeats that verify a few minutes early.

## Tests (Vitest)
- A spec sits beside the component or service it tests (`thing.spec.ts` beside `thing.ts`) and names the behaviour its acceptance criterion states.
- `cd web && npm test` runs them all (Vitest); while iterating, `npm test -- --watch=false -t "<name>"` runs one.
- Render through the same providers the app uses: `provideI18nTesting()` for translated text, `provideNativeDateAdapter()` for datepickers (see Forms and Languages above).
- A spec of a `resource()`-backed template covers loading, error and empty, as the template does.
- Node must be ≥ 22.22.3, ≥ 24.15.0, or ≥ 26 — Angular CLI 22 refuses older runtimes outright.

## Definition of done
- Only the generated client calls the backend (`web/src/app/api/<service>/`); no `HttpClient`, no URL, no call that bypasses the Gateway (ADR-013).
- API changed → `npm run gen:api`, one `npm run typecheck`, then `git diff -- web/src/app/api` keeps only the real schema delta.
- Vitest: a spec beside each changed component or service proves its acceptance criteria; `npm test` is green.
- Both UI languages: every new user-facing string is a key in both `src/i18n/en.json` and `src/i18n/pl.json`, and a spec rendering it uses `provideI18nTesting()`.
- Zero warnings, lint and format clean (`scripts/verify.mjs` runs `typecheck`, `lint`, `build`, `test` and `format:check`).
