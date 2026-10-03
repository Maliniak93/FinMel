namespace Skarbiec.Contracts.Events;

// A history backfill publishes nothing, so it has no member; Prices is the zero value an omitted field reads as.
public enum PriceSyncKind
{
    Prices = 0,
    Fx = 1,
}
