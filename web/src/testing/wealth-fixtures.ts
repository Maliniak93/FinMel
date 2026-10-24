import { vi } from 'vitest';

import type {
  AccountResponse,
  AssetGroupResponse,
  BondResponse,
  CashPlacesResponse,
  DepositResponse,
  MetalResponse,
  SecuritiesResponse,
} from '../app/api/portfolio';
import type { DashboardResponse } from '../app/api/reporting';

export const GROUP_ID = 'g0000000-0000-0000-0000-000000000001';

export const IDS = {
  brokerage: '11111111-1111-1111-1111-111111111111',
  bondsAccount: '22222222-2222-2222-2222-222222222222',
  archivedAccount: '33333333-3333-3333-3333-333333333333',
  bank: '44444444-4444-4444-4444-444444444444',
  bankCash: 'b4444444-4444-4444-4444-444444444444',
  deposit: 'd0000000-0000-0000-0000-000000000001',
  depositUnvalued: 'd0000000-0000-0000-0000-000000000002',
  bond: 'b0000000-0000-0000-0000-000000000001',
  security: 's0000000-0000-0000-0000-000000000001',
  archivedSecurity: 's0000000-0000-0000-0000-000000000002',
  metal: 'm0000000-0000-0000-0000-000000000001',
  wallet: 'c0000000-0000-0000-0000-000000000001',
  walletCash: 'c1000000-0000-0000-0000-000000000001',
  foreignJar: 'c0000000-0000-0000-0000-000000000002',
  foreignJarCash: 'c1000000-0000-0000-0000-000000000002',
} as const;

export const ASOF = '2026-10-20';

export function account(overrides: Partial<AccountResponse>): AccountResponse {
  return {
    id: IDS.brokerage,
    name: 'XTB IKE',
    type: 0,
    wrapper: 1,
    currency: 'PLN',
    isArchived: false,
    cashAssetId: 'a0000000-0000-0000-0000-000000000001',
    cashBalance: 2000,
    groupId: GROUP_ID,
    ...overrides,
  } as AccountResponse;
}

export interface WealthApiData {
  accounts: AccountResponse[];
  archivedAccounts: AccountResponse[];
  groups: AssetGroupResponse[];
  cashPlaces: CashPlacesResponse;
  deposits: DepositResponse[];
  bonds: BondResponse[];
  securities: SecuritiesResponse;
  archivedSecurities: SecuritiesResponse['holdings'];
  metals: MetalResponse[];
  dashboard: DashboardResponse;
}

export function wealthApiData(): WealthApiData {
  return {
    accounts: [
      account({}),
      account({
        id: IDS.bondsAccount,
        name: 'Obligacje IKE',
        type: 1,
        cashBalance: 0,
        groupId: null,
        cashAssetId: 'a0000000-0000-0000-0000-000000000002',
      }),
      account({
        id: IDS.bank,
        name: 'Santander',
        type: 2,
        cashBalance: 45000,
        groupId: null,
        cashAssetId: IDS.bankCash,
      }),
    ],
    archivedAccounts: [
      account({
        id: IDS.archivedAccount,
        name: 'Old broker',
        isArchived: true,
        cashBalance: 500,
        groupId: null,
        cashAssetId: 'a0000000-0000-0000-0000-000000000003',
      }),
    ],
    groups: [{ id: GROUP_ID, name: 'Emerytura', color: 'g1' } as unknown as AssetGroupResponse],
    cashPlaces: {
      places: [
        {
          id: IDS.wallet,
          name: 'Portfel',
          isArchived: false,
          groupId: null,
          valuePln: 800,
          balances: [{ currency: 'PLN', cashAssetId: IDS.walletCash, balance: 800, valuePln: 800 }],
        },
        {
          id: IDS.foreignJar,
          name: 'Słoik EUR',
          isArchived: false,
          groupId: null,
          valuePln: null,
          balances: [
            { currency: 'EUR', cashAssetId: IDS.foreignJarCash, balance: 100, valuePln: null },
          ],
        },
      ],
      totals: [],
      totalValuePln: 800,
      fxUnavailable: true,
    },
    deposits: [
      {
        assetId: IDS.deposit,
        name: 'Lokata Alior',
        isArchived: false,
        groupId: null,
        currency: 'PLN',
        principal: 50000,
        annualInterestRatePercent: 5,
        maturityDate: '2027-10-20',
        status: 0,
      },
      {
        assetId: IDS.depositUnvalued,
        name: 'Lokata bez wyceny',
        isArchived: false,
        groupId: null,
        currency: 'PLN',
        principal: 1000,
        annualInterestRatePercent: 4,
        maturityDate: '2027-01-01',
        status: 0,
      },
    ] as unknown as DepositResponse[],
    bonds: [
      {
        assetId: IDS.bond,
        accountId: IDS.bondsAccount,
        accountName: 'Obligacje IKE',
        isArchived: false,
        groupId: null,
        name: 'EDO0935',
        seriesCode: 'EDO0935',
        bondCount: 10,
        status: 0,
        periods: [],
        redemptions: [],
      },
    ] as unknown as BondResponse[],
    securities: {
      holdings: [
        {
          assetId: IDS.security,
          groupId: null,
          isArchived: false,
          accountId: IDS.brokerage,
          accountName: 'XTB IKE',
          name: 'CD Projekt',
          ticker: 'CDR.WA',
          currency: 'PLN',
          quantity: 70,
          lastPrice: 200,
          lastPriceDate: ASOF,
          valuePln: 14000,
          unrealizedPlPln: 500,
          unrealizedPlPercent: 3.7,
          priceUnavailableReason: null,
        },
      ],
      totals: { valuePln: 14000, costPln: 13500, unrealizedPlPln: 500 },
    } as unknown as SecuritiesResponse,
    archivedSecurities: [
      {
        assetId: IDS.archivedSecurity,
        groupId: null,
        isArchived: true,
        accountId: IDS.brokerage,
        accountName: 'XTB IKE',
        name: 'Old Corp',
        ticker: 'OLD.WA',
        currency: 'PLN',
        quantity: 10,
        lastPrice: 100,
        lastPriceDate: ASOF,
        valuePln: 1000,
        unrealizedPlPln: 0,
        unrealizedPlPercent: 0,
        priceUnavailableReason: null,
      },
    ] as unknown as SecuritiesResponse['holdings'],
    metals: [
      {
        assetId: IDS.metal,
        isArchived: false,
        groupId: null,
        name: 'Krugerrand',
        metal: 0,
        fineWeightGramsPerPiece: 31.1,
        pieces: 1,
        totalFineGrams: 31.1,
      },
    ] as unknown as MetalResponse[],
    dashboard: {
      netWorthPln: 115800,
      asOf: ASOF,
      isStale: false,
      byAssetClass: [],
      byAccount: [],
      byGroup: [],
      byAsset: [
        { assetId: IDS.bankCash, valuePln: 45000, priceDate: null, isStale: false },
        { assetId: IDS.deposit, valuePln: 50000, priceDate: null, isStale: false },
        { assetId: IDS.bond, valuePln: 1000, priceDate: null, isStale: false },
        { assetId: IDS.metal, valuePln: 3000, priceDate: ASOF, isStale: true },
      ],
    } as unknown as DashboardResponse,
  };
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

export interface WealthApiOptions {
  data?: WealthApiData;
  failing?: readonly string[];
  hang?: boolean;
}

export function mockWealthApi(options: WealthApiOptions = {}): ReturnType<typeof vi.spyOn> {
  const data = options.data ?? wealthApiData();
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    if (options.hang) {
      return new Promise<Response>(() => undefined);
    }
    const request = input as Request;
    const url = new URL(typeof input === 'string' ? input : request.url);
    const path = url.pathname;
    if (options.failing?.some((fragment) => path.includes(fragment))) {
      return json({ title: 'boom', detail: 'boom' }, 500);
    }
    const includeArchived = url.searchParams.get('includeArchived') === 'true';
    switch (path) {
      case '/api/portfolio/accounts':
        return json([...data.accounts, ...data.archivedAccounts]);
      case '/api/portfolio/cash-places':
        return json(data.cashPlaces);
      case '/api/portfolio/deposits':
        return json(data.deposits);
      case '/api/portfolio/bonds':
        return json(data.bonds);
      case '/api/portfolio/metals':
        return json(data.metals);
      case '/api/portfolio/securities':
        return url.searchParams.get('assetClass') === '2'
          ? json({
              ...data.securities,
              holdings: includeArchived
                ? [...data.securities.holdings, ...data.archivedSecurities]
                : data.securities.holdings,
            })
          : json({
              holdings: [],
              totals: { valuePln: 0, costPln: 0, unrealizedPlPln: 0 },
            });
      case '/api/portfolio/asset-groups':
        return json(data.groups);
      case '/api/reporting/dashboard':
        return json(data.dashboard);
      default:
        return json([]);
    }
  });
}

export function fetchedPaths(spy: ReturnType<typeof vi.spyOn>): string[] {
  return spy.mock.calls.map(
    ([input]: [unknown]) =>
      new URL(typeof input === 'string' ? input : (input as Request).url).pathname,
  );
}
