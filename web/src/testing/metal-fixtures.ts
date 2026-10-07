import type { MetalResponse } from '../app/api/portfolio';
import type { MetalPriceResponse } from '../app/api/marketdata';

export const METAL = { Gold: 0, Silver: 1 } as const;

export const OUNCE_IN_GRAMS = 31.1034768;

export function metalResponse(overrides: Partial<MetalResponse> = {}): MetalResponse {
  return {
    assetId: '11111111-eeee-1111-eeee-111111111111',
    portfolioId: '22222222-2222-2222-2222-222222222222',
    portfolioName: 'Vault',
    portfolioIsArchived: false,
    isArchived: false,
    name: 'Krugerrand',
    metal: METAL.Gold,
    fineWeightGramsPerPiece: OUNCE_IN_GRAMS,
    pieces: 2,
    totalFineGrams: 2 * OUNCE_IN_GRAMS,
    ...overrides,
  };
}

export const goldKrugerrand: MetalResponse = metalResponse();

export const silverBar: MetalResponse = metalResponse({
  assetId: '33333333-eeee-3333-eeee-333333333333',
  portfolioId: '44444444-4444-4444-4444-444444444444',
  portfolioName: 'Home',
  name: 'Silver bar',
  metal: METAL.Silver,
  fineWeightGramsPerPiece: 100,
  pieces: 5,
  totalFineGrams: 500,
});

export const metalPricesGoldOnly: MetalPriceResponse[] = [
  {
    metal: METAL.Gold,
    instrumentId: 'aaaaaaaa-0000-0000-0000-000000000001',
    date: '2026-10-05',
    pricePerGramUsd: 133.92,
    usdPlnRate: 3.9,
    pricePerGramPln: 522.29,
    pricePerTroyOuncePln: 16245.5,
    isStale: false,
  },
  {
    metal: METAL.Silver,
    instrumentId: 'aaaaaaaa-0000-0000-0000-000000000002',
    date: null,
    pricePerGramUsd: null,
    usdPlnRate: null,
    pricePerGramPln: null,
    pricePerTroyOuncePln: null,
    isStale: true,
  },
];
