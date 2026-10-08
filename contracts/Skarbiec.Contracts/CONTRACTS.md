# Contracts rules

`Skarbiec.Contracts` holds every cross-service event and DTO, plus the `Result`/`Result<T>`/
`Error` primitives (ADR-017). It contains only C# records and the validation needed to
enforce their invariants — no business logic, no service internals. Services depend on it
via `ProjectReference`; it depends on nothing else in the solution.

Validating factories (e.g. `Money.Create(...)`) return a failed `Result` instead of throwing
(ADR-017) — this project has no throwing constructors for value objects with invariants.

Consumers must tolerate fields they don't know about yet (forward compatibility) — see the
deserialization contract test in `Skarbiec.Contracts.Tests`. Everything else about compatibility
over time is suspended while ADR-019 (greenfield mode) holds — see the rules below.

## Rules

These wire-versioning rules govern the **event/DTO records** (e.g. `UserRegistered`), not the
shared primitives (`Money`, `AssetClass`, `AssetValuationMode`, `TreasuryBondType`, `SupportedCurrencies`,
`Result`/`Result<T>`/`Error`) — those never go on the wire and version like any other C# type.
`TreasuryBondType` is the MF retail bond type (`Ots … Rod`); its member order is the order MarketData lists
the bond offer in, so reordering it reorders the offer. `Metal` and `MetalInstruments` pin each precious metal
to its seeded MarketData instrument id, so changing an id orphans every stored reference to it.

1. **Edit the record in place** (ADR-019 — greenfield mode). The system has one user and no real
   data, so there is no old shape to stay compatible with: rename, retype or remove a field
   directly. No `V2` types, no deprecation window, no two shapes coexisting.
2. **Change every side in the same change.** A changed record means every publisher, every
   consumer and every test using it is updated together — the compiler is the compatibility check,
   and the build must be green before the change is done. The same goes for enums: a member may be
   removed or renumbered as long as everything reading it is updated.
3. **Consumers still tolerate unknown fields** (forward compatibility) — a payload with extra
   fields must deserialize without error, so an in-flight message from a not-yet-restarted service
   never poisons a queue. This is the one wire rule ADR-019 keeps; the deserialization tests in
   `Skarbiec.Contracts.Tests` (fixtures `*-with-extra-fields.json`) guard it.
4. **A `required` member must be in every payload.** System.Text.Json rejects a message missing a
   `required` property, so adding one (e.g. `AssetPositionChanged.IsArchived`, asset-archive) means
   updating the `*-with-extra-fields.json` fixture in the same change. A nullable, non-`required`
   member (e.g. `AssetPositionChanged.FirstTransactionDate`, the asset's earliest transaction date)
   needs no fixture change — a payload without it deserializes it as `null`.

When ADR-019 is revoked (real data in production), the classic discipline comes back: additive
only, never rename or remove, breaking change → a new `V2` record published alongside the old one.
Until then, don't pay for it.
