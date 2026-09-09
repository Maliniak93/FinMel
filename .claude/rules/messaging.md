---
paths:
  - "contracts/**"
  - "**/Messaging/**"
  - "**/Consumers/**"
  - "**/Sources/*Job*.cs"
---

# Messaging: events, outbox, consumers (ADR-012, ADR-021)

Full walkthrough and the poison-message story: `Skarbiec.ServiceDefaults/Messaging/README.md`.

## Event design

- An event is a **fact that already happened**: past-tense name (`AssetPositionChanged`, `AssetRemoved`, `DailyPricesSynced`), never a command or a request.
- Carry the **full state** a consumer needs — every id involved plus `UserId`, and the whole payload it will store. A consumer must never call the publishing service back to fill in the rest; that is the point of event-carried state transfer (ADR-021).
- Contracts are `record`s in `contracts/Skarbiec.Contracts/Events/`. Greenfield (ADR-019): **edit the record in place, never add a `V2`** — and update every publisher, every consumer and every test in the same change.
- Consumers must still tolerate an unknown field arriving (System.Text.Json ignores it by default; do not turn on strict handling).

## Publishing

- Inject `IPublishEndpoint` and `Publish` **before the same `SaveChangesAsync`** as the business write, so the outbox row commits in that one transaction. Never resolve `IBus` and send directly — an unbrokered publish escapes the outbox and can fire for a write that later rolls back.
- Wiring is one call: `builder.AddRabbitMqMessaging<WebApplicationBuilder, XDbContext>()` from ServiceDefaults (kebab-case endpoint names, EF outbox on Postgres, `UseBusOutbox`).
- Every slice that mutates a position publishes — including update and delete paths, not just create.

## Consuming

- `sealed class XConsumer(...) : IConsumer<XEvent>` plus an empty definition:
  ```csharp
  sealed class XConsumerDefinition : IdempotentConsumerDefinition<XConsumer, XDbContext>;
  ```
- Register both inside `AddRabbitMqMessaging`, passing the definition as a `Type`:
  ```csharp
  builder.AddRabbitMqMessaging<WebApplicationBuilder, XDbContext>(
      configureConsumers: x => x.AddConsumer<XConsumer>(typeof(XConsumerDefinition)));
  ```
  The two-generic form `AddConsumer<TConsumer, TDefinition>()` **does not compile here** — `IRegistrationConfigurator`'s own arity-1 `AddConsumer<T>` hides the extension method.
- The definition gives the endpoint inbox dedup by `MessageId`, a capped exponential retry, and the transport's `<queue>_error` queue once retry is exhausted. Keep the `Consume` body idempotent in spirit as well: the inbox stops redelivery, but anything the consumer calls outside its transaction can still run twice.
- A consumer writing for many users bypasses the tenancy filter with `IgnoreQueryFilters()` — comment why, right there.

## DbContext requirements

`OnModelCreating` must call `AddInboxStateEntity()`, `AddOutboxMessageEntity()` and `AddOutboxStateEntity()`, with **service-prefixed table names** (the test databases are shared, so unprefixed MassTransit tables collide), and a migration must exist for them.

## Required tests per event

1. **Outbox atomicity** — the outbox row and the business row commit together; build the provider with `HostlessOutboxProvider` so the delivery poller cannot race the assertion.
2. **Idempotency** — the same `MessageId` delivered twice runs the consumer body once.
3. **Contract deserialization** — a payload with an unknown extra field still deserializes into the record.

Trace context propagates automatically through MassTransit + OTel — never add correlation ids by hand.
