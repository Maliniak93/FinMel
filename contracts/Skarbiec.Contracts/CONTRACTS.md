# Contracts versioning rules

`Skarbiec.Contracts` holds every cross-service event and DTO, plus the `Result`/`Result<T>`/
`Error` primitives (ADR-017). It contains only C# records and the validation needed to
enforce their invariants — no business logic, no service internals. Services depend on it
via `ProjectReference`; it depends on nothing else in the solution.

Validating factories (e.g. `Money.Create(...)`) return a failed `Result` instead of throwing
(ADR-017) — this project has no throwing constructors for value objects with invariants.

Consumers must tolerate fields they don't know about yet (forward compatibility) — see the
deserialization contract test in `Skarbiec.Contracts.Tests`.

## Rules

These wire-versioning rules govern the **event/DTO records** (e.g. `UserRegistered`), not the
shared primitives (`Money`, `AssetClass`, `Result`/`Result<T>`/`Error`) — those never go on the
wire and version like any other C# type.

1. **Additive only.** A new field is always optional with a sensible default. Existing
   consumers that don't know about it must keep deserializing without error.
2. **Never rename or remove a field.** Both break every consumer still on the old shape.
3. **Breaking change → new type.** If a change can't be additive (field removed, type
   changed, semantics changed), ship a new record — `UserRegisteredV2`, not a mutated
   `UserRegistered`. Old and new versions can coexist and both get published/consumed
   during migration.
4. **Enums grow, they don't shrink.** Adding a member is safe; removing or renumbering
   one is a breaking change (see rule 3).
