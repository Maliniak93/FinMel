using Microsoft.EntityFrameworkCore;

namespace Skarbiec.MarketData.Data;

// No tenancy filter here: instruments/quotes are global reference data, not user-owned (ADR-006
// scope note in T0.13 — custom per-user instruments are revisited in Phase 2).
public sealed class MarketDataDbContext(DbContextOptions<MarketDataDbContext> options) : DbContext(options);
