import type { SecuritiesResponse } from '../../../api/portfolio';

export const PRICE_UNAVAILABLE_REASON = {
  NoQuote: 0,
  FxRateMissing: 1,
  MarketDataUnavailable: 2,
} as const;

export const vwce = {
  assetId: '11111111-1111-1111-1111-111111111111',
  accountId: '22222222-2222-2222-2222-222222222222',
  accountName: 'XTB IKE',
  name: 'Vanguard FTSE All-World',
  instrumentId: '33333333-3333-3333-3333-333333333333',
  ticker: 'VWCE',
  exchange: 'XETRA',
  currency: 'EUR',
  quantity: 15,
  averageBuyPrice: 110,
  costPln: 6780,
  lastPrice: 120,
  lastPriceDate: '2026-01-30',
  valuePln: 7740,
  unrealizedPl: 150,
  unrealizedPlPercent: 9.09,
  unrealizedPlPln: 960,
};

export const cdr = {
  assetId: '44444444-4444-4444-4444-444444444444',
  accountId: '55555555-5555-5555-5555-555555555555',
  accountName: 'XTB zwykly',
  name: 'CD Projekt',
  instrumentId: '66666666-6666-6666-6666-666666666666',
  ticker: 'CDR',
  exchange: 'GPW',
  currency: 'PLN',
  quantity: 10,
  averageBuyPrice: 200,
  costPln: 2000,
  lastPrice: 150,
  lastPriceDate: '2026-01-30',
  valuePln: 1500,
  unrealizedPl: -500,
  unrealizedPlPercent: -25,
  unrealizedPlPln: -500,
};

export const noRateCost = {
  ...vwce,
  assetId: '77777777-7777-7777-7777-777777777777',
  name: 'Xetra ETF bought early',
  ticker: 'XETF',
  costPln: null,
  unrealizedPlPln: null,
};

export function unavailable(
  assetId: string,
  name: string,
  reason: number,
): typeof vwce & { priceUnavailableReason: number } {
  return {
    ...vwce,
    assetId,
    name,
    lastPrice: null as never,
    lastPriceDate: null as never,
    valuePln: null as never,
    unrealizedPl: null as never,
    unrealizedPlPercent: null as never,
    unrealizedPlPln: null as never,
    priceUnavailableReason: reason,
  };
}

export const stocks: SecuritiesResponse = {
  holdings: [vwce, cdr],
  totals: { valuePln: 9240, costPln: 8780, unrealizedPlPln: 460 },
} as SecuritiesResponse;

export const etfs: SecuritiesResponse = {
  holdings: [{ ...vwce, assetId: '88888888-8888-8888-8888-888888888888', name: 'World ETF' }],
  totals: { valuePln: 7740, costPln: 6780, unrealizedPlPln: 960 },
} as SecuritiesResponse;

export const empty: SecuritiesResponse = {
  holdings: [],
  totals: { valuePln: 0, costPln: 0, unrealizedPlPln: 0 },
} as SecuritiesResponse;
