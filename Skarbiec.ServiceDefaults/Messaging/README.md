# Messaging: idempotent consumers (T0.12)

`AddRabbitMqMessaging<TBuilder, TDbContext>` (in `MessagingExtensions.cs`) wires MassTransit v8 +
RabbitMQ + the EF Core outbox for a service. This note covers the other half: consuming safely.

## Why a consumer needs this

RabbitMQ (and MassTransit's own retry) can redeliver a message you already handled — after a
broker restart, a NACK, an ack that never reached the broker, or a manual redrive from the error
queue. A consumer must not re-run its side effect just because the same message arrived twice
(ADR-012: "consumers idempotent").

## The template

`IdempotentConsumerDefinition<TConsumer, TDbContext>` (in `MessagingExtensions.cs`) is a
`ConsumerDefinition<TConsumer>` that wires, on the consumer's own receive endpoint:

1. A capped exponential retry (`UseMessageRetry`) — a few spaced-out attempts for a transient
   failure (a dependency being briefly unavailable, a deadlock, ...).
2. `UseEntityFrameworkOutbox<TDbContext>` on that same receive endpoint — this is the *inbox* half
   of the outbox package: it deduplicates by `MessageId` against the `InboxState` table before the
   consumer body runs a second time, and wraps the consume in a DB transaction against
   `TDbContext`.

## Checklist to add a new consumer

1. Make sure `TDbContext`'s `OnModelCreating` calls `builder.AddInboxStateEntity()` (alongside
   `AddOutboxMessageEntity()`/`AddOutboxStateEntity()` — see `IdentityDbContext` for the pattern
   from T0.10) and that a migration exists for it.
2. Write the consumer: `sealed class MyConsumer(...) : IConsumer<MyEvent> { ... }`. Keep
   `Consume` itself idempotent in spirit too — the inbox stops *redelivery*, but if the handler
   calls out to something outside this transaction (another service, a queue), that call can still
   happen twice on legitimate retries before the first attempt's inbox row commits.
3. Declare an (often empty) definition:

   ```csharp
   sealed class MyConsumerDefinition : IdempotentConsumerDefinition<MyConsumer, MyDbContext>;
   ```

4. Register both together — **not** `AddConsumer<TConsumer, TDefinition>()`; that two-generic-arg
   extension method is hidden by `IRegistrationConfigurator`'s own arity-1 `AddConsumer<T>`, so it
   won't compile. Pass the definition as a `Type` instead:

   ```csharp
   builder.AddRabbitMqMessaging<WebApplicationBuilder, MyDbContext>(
       configureConsumers: x => x.AddConsumer<MyConsumer>(typeof(MyConsumerDefinition)));
   ```

## What "poisoned" looks like

Once retry is exhausted, MassTransit's RabbitMQ transport moves the message to the endpoint's
default `<queue>_error` queue and moves on — the main queue is never blocked behind a message that
keeps failing. Nothing else to configure for that; it's the transport's default behavior once a
retry policy is in place.

## Tests

`Skarbiec.Identity.Tests`:

- `UserRegisteredIdempotentConsumerTests` — same `MessageId` delivered twice, consumer body runs
  once (verified red without the template: `ConsumeCount == 2`).
- `UserRegisteredPoisonMessageTests` — an always-failing message ends up on `<queue>_error` after
  retry is exhausted, and a message published afterwards to the same queue still gets consumed
  normally.

Both build their own `ServiceProvider` (rather than `IdentityApiFactory`) with a throwaway
consumer on a queue name unique to the test class, mirroring `UserRegisteredOutboxDurabilityTests`
from T0.11 — see that file for why (queues are durable and outlive one test class on the shared
RabbitMQ container).
