---
name: new-event
description: Add an integration event - contract in Skarbiec.Contracts, outbox publish, idempotent consumer, contract tests.
argument-hint: <EventName> <Publisher> [Consumer]
disable-model-invocation: true
---

Add the integration event described by `$ARGUMENTS`.

If `Skarbiec.sln` does not exist yet, stop: implementation has not started — point at T0.1 in `skarbiec-plan/zadania/phase-0-platform.md`.

## Steps

1. Contract: C# record in `contracts/Skarbiec.Contracts`. Changing an existing record means editing it in place — no `V2` type, no deprecation window (ADR-019); update every publisher, consumer and test of that record in the same change. Consumers must still tolerate unknown extra fields (forward compatibility).
2. Publish only through MassTransit EF Outbox: the event is saved in the same transaction as the data and published after commit. Never call `IPublishEndpoint` outside the outbox.
3. Consumer must be idempotent: inbox/dedup by `MessageId`; double delivery must not duplicate data.
4. Tests:
   - contract test: a payload with unknown extra fields still deserializes (forward compatibility)
   - outbox test: killing the process between commit and publish does not lose the event
   - idempotency test: delivering the same event twice has no extra effect
5. Trace context propagates automatically through MassTransit — do not add manual correlation IDs.
