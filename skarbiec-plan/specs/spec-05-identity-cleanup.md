---
title: Identity cleanup — remove unused BaseCurrency
status: draft
tier: 1
branch: feat/identity-cleanup
created: 2026-09-06
---

## Goal

`ApplicationUser.BaseCurrency` (and its EF mapping) is gone; Identity carries no per-user currency field.

## Why

Część I dead-code list. Skarbiec is PLN-only (ADR-008) — `Money.BaseCurrency` in `Skarbiec.Contracts` is already the single source of that fact. `ApplicationUser.BaseCurrency` duplicates it per-user with a hardcoded default and is never read by any handler, never exposed on any request/response DTO, and never rendered in the UI — confirmed below.

## Scope

### Backend

- `services/Identity/Skarbiec.Identity/Data/ApplicationUser.cs`: remove the `BaseCurrency` property (drop the now-unused `using Skarbiec.Contracts;` if nothing else in the file needs it).
- `services/Identity/Skarbiec.Identity/Data/IdentityDbContext.cs`: remove the `user.Property(u => u.BaseCurrency).HasMaxLength(3).HasDefaultValue(Money.BaseCurrency);` line from `OnModelCreating`.
- New migration: `dotnet ef migrations add DropBaseCurrency --project services/Identity/Skarbiec.Identity --startup-project services/Identity/Skarbiec.Identity`, then `dotnet format`.

### Frontend

None. Confirmed by reading `RegisterRequest.cs`/`RegisterResponse.cs` (Register), `LoginResponse.cs` (Login — via `RefreshResponse.cs`/`LoginRequest.cs` too), `contracts/Skarbiec.Contracts/Events/UserRegistered.cs`, and `web/src/app/features/auth/register/register.ts`: none carries a currency field. The Identity OpenAPI shape is unchanged, so no client regeneration is needed.

## Out of scope

`Money.BaseCurrency` (the `"PLN"` constant in `Skarbiec.Contracts`, ADR-008) — stays exactly as is; it is a different symbol in a different class from the field being removed here. Any multi-currency-per-user feature — not planned. Squashing Identity's migration history into one `InitialCreate` (spec-06's job, which runs after this one).

## Design decisions

1. **No API/event shape change.** Read every Identity request/response record (`RegisterRequest`, `RegisterResponse`, `LoginRequest`, `LoginResponse`, `RefreshResponse`) and `contracts/Skarbiec.Contracts/Events/UserRegistered.cs`: none has a currency field today. `ApplicationUser.BaseCurrency` only ever existed as an EF-mapped column with a hardcoded default (`Money.BaseCurrency`) — dead from day one. This means: no `web/openapi-ts.config.ts` impact, no `npm run gen:api` needed, and register/login/refresh/logout tests need zero changes (confirmed by reading `RegisterEndpointTests.cs`, which never references it).
2. **Migration history is not rewritten here.** `BaseCurrency` legitimately still appears in the three pre-existing migrations (`InitialCreate`, `AddRefreshTokens`, `AddMassTransitOutbox` — their `.cs`/`.Designer.cs` files) as a historical record of past schema, and the new `DropBaseCurrency.cs` migration's own `Up()` legitimately names the column it drops (`migrationBuilder.DropColumn(name: "BaseCurrency", table: "AspNetUsers")`). Neither is a defect — the invariant that must hold is that the **current model surface** (`ApplicationUser.cs`, `IdentityDbContext.cs`, and `IdentityDbContextModelSnapshot.cs`, which always reflects current model state) has zero references. The acceptance criteria below grep exactly that surface, not the whole `Migrations/` history.
3. Per ADR-019, no data backfill is written — a single additive `DropColumn` migration is enough; no local database reset is required for this spec alone (unlike spec-06's squash), since it's one migration appended to existing history, applied automatically by each service's own `MigrateAsync()` on next `dotnet run --project Skarbiec.AppHost`.

## Data / API changes

| Entity / endpoint | Change | Migration? |
|---|---|---|
| `ApplicationUser.BaseCurrency` | Removed | Yes — `DropBaseCurrency` |
| `IdentityDbContext` model config for `BaseCurrency` | Removed | (same migration) |

## Acceptance criteria

1. Given the property and its mapping removed, when the solution builds, then it succeeds. — proof: `dotnet build Skarbiec.slnx`
2. Given the current model surface no longer references it, when grepped, then nothing remains outside migration history. — proof: `grep -rln BaseCurrency --include=*.cs services/Identity/Skarbiec.Identity --exclude-dir=Migrations ; test $? -ne 0` and `grep -q BaseCurrency services/Identity/Skarbiec.Identity/Migrations/IdentityDbContextModelSnapshot.cs ; test $? -ne 0`
3. Given the frontend never referenced it, when grepped, then it still doesn't. — proof: `grep -rn BaseCurrency web/src ; test $? -ne 0`
4. Given the new migration, when the Migrations folder is listed, then exactly one new `DropBaseCurrency` pair exists. — proof: `ls services/Identity/Skarbiec.Identity/Migrations/*DropBaseCurrency*`
5. Given Register/Login/Refresh/Logout are unaffected, when their tests run, then all stay green unmodified. — proof: `dotnet test services/Identity/Skarbiec.Identity.Tests/Skarbiec.Identity.Tests.csproj`
6. Given `Money.BaseCurrency` is untouched, when `Skarbiec.Contracts.Tests` runs, then `MoneyTests` stays green. — proof: `dotnet test contracts/Skarbiec.Contracts.Tests/Skarbiec.Contracts.Tests.csproj --filter "FullyQualifiedName~MoneyTests"`
7. Given the affected project, when verified, then it is green. — proof: `node scripts/verify.mjs --projects Identity`

## Verification

```bash
dotnet build Skarbiec.slnx
dotnet test services/Identity/Skarbiec.Identity.Tests/Skarbiec.Identity.Tests.csproj
dotnet format Skarbiec.slnx --verify-no-changes
node scripts/verify.mjs --projects Identity
```

No manual UI smoke needed — the register/login forms never surfaced this field.

## Risks / open questions

_(none — required empty before `status: approved`)_

## Result

<!-- Filled in by ops after Ship. -->
