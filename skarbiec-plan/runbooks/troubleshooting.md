# Troubleshooting

Known failure modes, what causes them, and the fix. If you hit something not listed here, add it once you've solved it.

## Postgres / RabbitMQ show "Unhealthy" in the Aspire dashboard

**Cause:** Postgres and RabbitMQ bake their admin credentials into the data volume the *first* time they initialize and never change them again. The AppHost uses **fixed** literal passwords (not Aspire's generated-and-persisted-to-user-secrets default) specifically so this can't drift — but if a fixed password is ever edited in code without also resetting local state, the volume still expects the old value and every connection fails authentication, with no self-healing.
**Fix:** remove the named Postgres/RabbitMQ volumes so they reinitialize cleanly (`docker volume ls` to find the names, then `docker volume rm ...`) — see `runbooks/local-dev.md` → "Resetting local databases". Always safe: no real data exists in local-only development.

## Gateway 429s during a seed script or smoke test

**Cause:** the Gateway rate-limits at 100 requests / 10 seconds. A seed or smoke script that fires requests in a tight loop (e.g. creating many assets) can hit it well before a human would.
**Fix:** throttle/batch the script's requests, or run it in smaller chunks. Don't raise the global limit for a script's convenience.

## `npm run gen:api` rewrites unrelated imports

**Cause:** `@hey-api/openapi-ts` occasionally drops the `.js` extension from every generated import, repo-wide, as a side effect of regenerating — not just the files touched by the actual schema change.
**Fix:** always run `npm run typecheck` right after `gen:api`, then read the diff of `src/app/api/` and keep only the real schema delta; hand-revert the extension-drop noise. Don't commit the raw regenerated output unreviewed.

## A wall of whitespace-only diff on Windows

**Cause:** historically, checked-out CRLF vs. committed LF. **Already fixed**: `.gitattributes` (`* text=auto eol=lf`, plus explicit `binary` rules for images/fonts/PDF) normalizes this repo-wide.
**Fix if it ever recurs** (e.g. a new file type needs a binary rule): update `.gitattributes`, then `git add --renormalize .` once.

## Reporting health check flakes right after startup

**Cause:** `/health/ready` can report unready for a few seconds while EF migrations apply and MassTransit finishes standing up its outbox/inbox infrastructure.
**Fix:** poll `/health/ready` for up to 10 seconds before treating a "not ready" as a real failure, in scripts and smoke tests alike — don't fail on the first check.

## A generated EF migration touches `xmin`

**Cause:** `xmin` is a shadow property used only as the optimistic-concurrency token; it's a Postgres system column, not a real one, and should never appear in migration DDL.
**Fix:** if `dotnet ef migrations add` generates lines creating, dropping or altering `xmin`, delete those lines by hand before applying the migration.

## An enum-backed column doesn't default the way you expect

**Cause:** enums are stored as their underlying `int`. `HasDefaultValue` only sets the *database* default — an existing row (or one inserted outside EF) gets the CLR default, which is always `0`. If the enum's `0` member isn't the one meant to be "unset" or default, reordering members later silently rewrites the meaning of existing data.
**Fix:** always give the enum member with value `0` the meaning "not set" (or the intended default), and never reorder existing members — only append.

## Host fails at startup with a "two body parameters" binding error

**Cause:** a slice's handler class was added but never registered in DI (`builder.Services.AddScoped<XHandler>()` in `Program.cs`). The build stays green — this only fails at runtime, when Minimal API tries to bind the unregistered handler as a second `[FromBody]` parameter.
**Fix:** register every handler next to the others in `Program.cs`. If a service won't start and the error mentions multiple body parameters, this is almost always why.

## `npm start` / `npm test` fails oddly on an unlisted Node version

**Cause:** Angular CLI 22 requires Node 22.22.3+, 24.15.0+, or 26+ — versions between or below those minors (e.g. a stray Node 20, or 22.10) fail in ways that don't always name the real cause.
**Fix:** check `node -v` against the three floors above before debugging anything else in the frontend toolchain.
