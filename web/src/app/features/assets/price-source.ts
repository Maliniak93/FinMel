// Backend enum (Skarbiec.MarketData.Data.PriceSource) serializes as its underlying int — same
// declaration order (Nbp, Stooq, CoinGecko) as the C# enum.
const PRICE_SOURCE_LABELS: readonly string[] = ['NBP', 'Stooq', 'CoinGecko'];

export function priceSourceLabel(value: number): string {
  return PRICE_SOURCE_LABELS[Number(value)] ?? 'Unknown';
}

// Nbp is deliberately excluded here — AddCustomInstrumentHandler rejects it (NBP only serves its
// fixed FX-table/gold endpoints, not arbitrary user tickers, see T2.8).
export const CUSTOM_INSTRUMENT_SOURCES: readonly { value: number; label: string }[] = [
  { value: 1, label: 'Stooq' },
  { value: 2, label: 'CoinGecko' },
];
