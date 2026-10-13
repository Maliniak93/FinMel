import type { WatchlistHoldingResponse, WatchlistItemResponse } from '../../../api/portfolio';
import { ACCOUNT_TYPE } from '../../accounts/account-type';
import { ASSET_CLASS } from '../../assets/asset-class';
import { requestUrl } from '../../assets/asset-form/testing/asset-form-fixtures';

export const watchlistItemId = 'b1111111-1111-1111-1111-111111111111';
export const watchedInstrumentId = 'b2222222-2222-2222-2222-222222222222';

export const accountAId = 'a1111111-1111-1111-1111-111111111111';
export const accountBId = 'a2222222-2222-2222-2222-222222222222';
export const accountCId = 'a4444444-4444-4444-4444-444444444444';

export const assetInAId = 'c1111111-1111-1111-1111-111111111111';
export const assetInBId = 'c2222222-2222-2222-2222-222222222222';
export const newAssetId = 'c3333333-3333-3333-3333-333333333333';

export const watchlistAccounts = [
  {
    id: accountAId,
    name: 'XTB zwykly',
    type: ACCOUNT_TYPE.Brokerage,
    wrapper: 0,
    isArchived: false,
  },
  { id: accountBId, name: 'XTB IKE', type: ACCOUNT_TYPE.Brokerage, wrapper: 1, isArchived: false },
  {
    id: accountCId,
    name: 'Stary broker',
    type: ACCOUNT_TYPE.Brokerage,
    wrapper: 0,
    isArchived: true,
  },
];

export const holdingInA: WatchlistHoldingResponse = {
  accountId: accountAId,
  accountName: 'XTB zwykly',
  assetId: assetInAId,
  assetName: 'Vanguard FTSE All-World',
  quantity: 10,
};

export const holdingInB: WatchlistHoldingResponse = {
  accountId: accountBId,
  accountName: 'XTB IKE',
  assetId: assetInBId,
  assetName: 'Vanguard FTSE All-World',
  quantity: 5,
};

export function watchlistItem(
  overrides: Partial<WatchlistItemResponse> = {},
): WatchlistItemResponse {
  return {
    id: watchlistItemId,
    instrumentId: watchedInstrumentId,
    assetClass: ASSET_CLASS.Etf,
    ticker: 'VWRA',
    name: 'Vanguard FTSE All-World',
    exchange: 'LSE',
    quoteCurrency: 'USD',
    note: 'Core position',
    targetPrice: 125,
    lastPrice: 120,
    lastPriceDate: '2026-08-04',
    previousClose: 118.5,
    dailyChange: 1.5,
    dailyChangePercent: 1.27,
    lastPricePln: 440.12,
    targetDistancePercent: -4,
    atOrBelowTarget: true,
    priceUnavailableReason: null,
    holdings: [holdingInA, holdingInB],
    heldQuantity: 15,
    ...overrides,
  };
}

export function methodOf(input: unknown): string {
  return typeof input === 'string' ? 'GET' : (input as Request).method;
}

export function pathOf(input: unknown): string {
  return new URL(requestUrl(input)).pathname;
}

export function isWatchlistList(input: unknown): boolean {
  return methodOf(input) === 'GET' && /\/api\/portfolio\/watchlist(\?|$)/.test(requestUrl(input));
}
