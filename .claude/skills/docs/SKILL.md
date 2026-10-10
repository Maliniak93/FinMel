---
name: docs
description: Regenerate the local offline Polish code guide (skarbiec-plan/przewodnik/) - extracts facts from the code, writes the prose of every stale page, builds the HTML. Writes only under skarbiec-plan/przewodnik/.
argument-hint: "[--all]"
disable-model-invocation: true
model: sonnet
effort: medium
allowed-tools: Bash, Read, Write, Grep, Glob
---

Regenerate the code guide. Talk to the user in Polish; write the prose fragments in Polish.

## Steps

1. Run `node scripts/docs.mjs facts $ARGUMENTS`. The last line `DOCS_RESULT: {json}` lists `stale` pages
   (prose missing or stamped with an older facts hash). Nothing stale -> say so and stop.
2. For each stale page run `node scripts/docs.mjs facts --page <slug>` to read its facts, then write
   `skarbiec-plan/przewodnik/.work/prose/<slug>.html`:
   - first line exactly `<!-- facts:<hash> -->`, with the `hash` of that page from
     `skarbiec-plan/przewodnik/.work/facts.json` (`pages.<slug>.hash`);
   - then plain HTML (`<p>`, `<ul>`, `<h2>`); the generator already renders the tables and the diagram,
     so the prose explains and does not repeat them;
   - every statement comes from the facts or from the code you read - never invent a service, endpoint
     or event.
3. Page content:
   - `index` - what Skarbiec is (personal wealth management, learning project with 4 services and a
     gateway) and how to read the guide; one short paragraph.
   - `architektura` - what each service does (Identity, Portfolio, MarketData, Reporting, Gateway) and why
     REST is used only for `/internal` lookups and batches while domain facts travel as events: ADR-001
     (4 services), ADR-012 (outbox events with full state), ADR-015, ADR-021 and ADR-027 (`/internal`
     REST, anonymous, no token). Read those ADRs in `skarbiec-plan/decisions.md` before citing them.
   - `eventy` - why events carry full state (ADR-012, ADR-021) and exactly four
     `<pre class="mermaid">sequenceDiagram ...</pre>` blocks (`build` fails with fewer than four): (1) buying
     10 shares for 1 500 PLN through a cash account -> `RecordTransaction` -> `AssetPositionChanged` ->
     Reporting revalues the dashboard, (2) registration and login, (3) the daily price sync, (4) the
     net-worth history rebuild. Read the handlers, consumers and jobs involved before drawing a step; the
     generated flowchart and message tables are not repeated.
   - `bazy-danych` - one short paragraph per service on what its database holds, the meaning of
     "filtr tenancy" vs "UserId bez filtra" (ADR-006) and the MassTransit outbox/inbox tables. The
     generator draws the ERDs.
   - `joby-integracje-frontend` - for each job say in plain Polish when it runs (translate the effective cron,
     mention that Development runs more often), which external API it fetches and why some work runs only
     on demand or at startup; one paragraph on how the UI reaches the backend (routes -> generated client ->
     Gateway, ADR-013). Read the job and source classes before describing them; the tables are not repeated.
   - `slabe-punkty` - information for a human deciding on an `/idea`, never a ready spec. The prose must
     contain `<section class="ocena-claude">` (`build` fails without it) holding your own assessment of
     complex or duplicated areas, each point linking to the page it concerns (`<a href="eventy.html#...">`);
     keep it visibly separate from the generated, deterministic list below it and say which is which.
4. Run `node scripts/docs.mjs build`. A failure names the page and leaves the previous guide untouched:
   fix the fragment (usually a wrong or missing stamp) and run `build` again.
5. Report in at most three lines: which pages were rewritten, the path
   `skarbiec-plan/przewodnik/index.html`, the commit shown in its footer.

## Boundaries

Write only under `skarbiec-plan/przewodnik/`. Edit no source file, run no git mutation, install nothing.
