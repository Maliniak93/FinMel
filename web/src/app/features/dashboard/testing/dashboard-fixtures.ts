import type { Type } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import type {
  AccountResponse,
  AssetGroupResponse,
  CashPlacesResponse,
} from '../../../api/portfolio';
import { client as reportingClient } from '../../../api/reporting/client.gen';
import type { DashboardResponse } from '../../../api/reporting';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { toDateOnly } from '../../../shared/date-only';

export const RETIREMENT_ID = '11111111-1111-1111-1111-111111111111';

export const dashboard: DashboardResponse = {
  netWorthPln: 15000,
  asOf: '2026-08-09',
  isStale: false,
  byAssetClass: [
    { assetClass: 0, valuePln: 5000, percentage: 33.33 },
    { assetClass: 2, valuePln: 10000, percentage: 66.67 },
  ],
  byAccount: [
    {
      accountId: RETIREMENT_ID,
      valuePln: 15000,
      snapshotDate: '2026-08-09',
      isStale: false,
      percentage: 100,
    },
  ],
  byGroup: [
    {
      groupId: null,
      valuePln: 15000,
      percentage: 100,
      byAssetClass: [
        { assetClass: 0, valuePln: 5000 },
        { assetClass: 2, valuePln: 10000 },
      ],
    },
  ],
};

export const RETIREMENT_GROUP_ID = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
export const INVESTMENTS_GROUP_ID = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
export const CUSHION_GROUP_ID = 'cccccccc-cccc-cccc-cccc-cccccccccccc';

export const groups: AssetGroupResponse[] = [
  {
    id: RETIREMENT_GROUP_ID,
    name: 'Emerytura',
    color: 'blue',
    targetPln: null,
    assetCount: 2,
    kind: 0,
    monthlyContributionPln: 0,
    sharePercent: 0,
    classAllocation: [],
  },
  {
    id: INVESTMENTS_GROUP_ID,
    name: 'Inwestycje',
    color: 'green',
    targetPln: null,
    assetCount: 1,
    kind: 0,
    monthlyContributionPln: 0,
    sharePercent: 0,
    classAllocation: [],
  },
  {
    id: CUSHION_GROUP_ID,
    name: 'Poduszka',
    color: 'orange',
    targetPln: 30000,
    assetCount: 1,
    kind: 1,
    monthlyContributionPln: 0,
    sharePercent: null,
    classAllocation: [],
  },
];

type GroupEntry = Omit<DashboardResponse['byGroup'][number], 'byAssetClass'> &
  Partial<Pick<DashboardResponse['byGroup'][number], 'byAssetClass'>>;

export function dashboardWithGroups(
  byGroup: GroupEntry[] = [
    { groupId: RETIREMENT_GROUP_ID, valuePln: 75000, percentage: 46.88 },
    { groupId: INVESTMENTS_GROUP_ID, valuePln: 55000, percentage: 34.38 },
    { groupId: CUSHION_GROUP_ID, valuePln: 28000, percentage: 17.5 },
    { groupId: null, valuePln: 2000, percentage: 1.25 },
  ],
): DashboardResponse {
  return {
    ...dashboard,
    netWorthPln: 160000,
    byGroup: byGroup.map((entry) => ({ ...entry, byAssetClass: entry.byAssetClass ?? [] })),
  };
}

export const dashboardWithClasses: DashboardResponse = {
  ...dashboard,
  byAssetClass: [
    { assetClass: 3, valuePln: 4000, percentage: 26.67 },
    { assetClass: 2, valuePln: 6000, percentage: 40 },
    { assetClass: 0, valuePln: 5000, percentage: 33.33 },
  ],
};

export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

export function requestUrl(input: unknown): string {
  return typeof input === 'string' ? input : (input as Request).url;
}

export function accountResponse(overrides: Partial<AccountResponse> = {}): AccountResponse {
  return {
    id: RETIREMENT_ID,
    name: 'Retirement',
    type: 0,
    wrapper: 0,
    currency: 'PLN',
    isArchived: false,
    cashAssetId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
    cashBalance: 0,
    ...overrides,
  };
}

export function cashPlacesResponse(
  totals: CashPlacesResponse['totals'],
  totalValuePln?: number,
): CashPlacesResponse {
  return { places: [], totals, totalValuePln, fxUnavailable: false };
}

export function dateOnlyFromToday(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return toDateOnly(date);
}

export const API_PATHS = {
  dashboard: '/api/reporting/dashboard',
  history: '/net-worth-history',
  accounts: '/api/portfolio/accounts',
  groups: '/api/portfolio/asset-groups',
  cash: '/api/portfolio/cash-places',
  deposits: '/api/portfolio/deposits',
  bonds: '/api/portfolio/bonds',
  planningMonth: '/api/portfolio/planning/months',
  todo: '/api/portfolio/planning/todo',
  overdue: '/api/portfolio/planning/overdue',
} as const;

export type ApiRoute = keyof typeof API_PATHS;

// A Response body is read once, so a plain Response is cloned for each call and a function builds a fresh one.
export type Responder = Response | ((url: string) => Response);

export function planningMonthResponse(
  overrides: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    incomes: {
      occurrences: [],
      balance: { planPln: 0, actualPln: 0, pendingPln: 0, differencePln: 0 },
      fxUnavailable: false,
      definitions: [],
    },
    expenses: { payments: [], costPln: 0, definitions: [] },
    freeFunds: { planPln: 0, forecastPln: 0, incomplete: false },
    ...overrides,
  };
}

const EMPTY_ROUTES: Record<ApiRoute, Responder> = {
  dashboard: jsonResponse(dashboard),
  history: jsonResponse({ range: '1Y', points: [] }),
  accounts: jsonResponse([]),
  groups: jsonResponse([]),
  cash: jsonResponse(cashPlacesResponse([])),
  deposits: jsonResponse([]),
  bonds: jsonResponse([]),
  planningMonth: jsonResponse(planningMonthResponse()),
  todo: jsonResponse({ items: [], paymentLeadDays: 7 }),
  overdue: jsonResponse({ items: [] }),
};

export function mockApi(overrides: Partial<Record<ApiRoute, Responder>> = {}) {
  const routes = { ...EMPTY_ROUTES, ...overrides };
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = requestUrl(input);
    const route = (Object.keys(API_PATHS) as ApiRoute[]).find((key) =>
      url.includes(API_PATHS[key]),
    );
    if (route === undefined) {
      throw new Error(`Unexpected request: ${url}`);
    }
    const responder = routes[route];
    return typeof responder === 'function' ? responder(url) : responder.clone();
  });
}

export function lastHistoryParams(fetchSpy: ReturnType<typeof mockApi>): URLSearchParams {
  const url = fetchSpy.mock.calls
    .map(([input]) => requestUrl(input))
    .filter((candidate) => candidate.includes(API_PATHS.history))
    .at(-1);
  if (url === undefined) {
    throw new Error('No net-worth history request was made.');
  }
  return new URL(url).searchParams;
}

export async function renderCard<T>(
  card: Type<T>,
  overrides: Partial<Record<ApiRoute, Responder>> = {},
): Promise<{ fixture: ComponentFixture<T>; fetchSpy: ReturnType<typeof mockApi> }> {
  portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  reportingClient.setConfig({ baseUrl: 'https://example.test' });
  const fetchSpy = mockApi(overrides);

  await TestBed.configureTestingModule({
    imports: [card],
    providers: [provideRouter([]), provideI18nTesting()],
  }).compileComponents();

  const fixture = TestBed.createComponent(card);
  await fixture.whenStable();
  return { fixture, fetchSpy };
}
