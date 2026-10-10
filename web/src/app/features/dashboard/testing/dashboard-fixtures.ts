import type { Type } from '@angular/core';
import { type ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import type { CashAccountsResponse, PortfolioResponse } from '../../../api/portfolio';
import { client as reportingClient } from '../../../api/reporting/client.gen';
import type { DashboardResponse } from '../../../api/reporting';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { toDateOnly } from '../../../shared/date-only';

export const RETIREMENT_ID = '11111111-1111-1111-1111-111111111111';
export const SAVINGS_ID = '33333333-3333-3333-3333-333333333333';
export const ARCHIVED_ID = '44444444-4444-4444-4444-444444444444';

export const dashboard: DashboardResponse = {
  netWorthPln: 15000,
  asOf: '2026-08-09',
  isStale: false,
  byAssetClass: [
    { assetClass: 0, valuePln: 5000, percentage: 33.33 },
    { assetClass: 2, valuePln: 10000, percentage: 66.67 },
  ],
  byPortfolio: [
    {
      portfolioId: RETIREMENT_ID,
      valuePln: 15000,
      snapshotDate: '2026-08-09',
      isStale: false,
      percentage: 100,
    },
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

export function portfolioResponse(overrides: Partial<PortfolioResponse> = {}): PortfolioResponse {
  return {
    id: RETIREMENT_ID,
    name: 'Retirement',
    description: null,
    currency: 'PLN',
    isArchived: false,
    assetCount: 1,
    ...overrides,
  };
}

export function cashAccountsResponse(totals: CashAccountsResponse['totals']): CashAccountsResponse {
  return { accounts: [], totals };
}

export function dateOnlyFromToday(days: number): string {
  const date = new Date();
  date.setDate(date.getDate() + days);
  return toDateOnly(date);
}

export const API_PATHS = {
  dashboard: '/api/reporting/dashboard',
  history: '/net-worth-history',
  portfolios: '/api/portfolio/portfolios',
  cash: '/api/portfolio/cash-accounts',
  deposits: '/api/portfolio/deposits',
  bonds: '/api/portfolio/bonds',
  savings: '/api/portfolio/savings-accounts',
} as const;

export type ApiRoute = keyof typeof API_PATHS;

// A Response body is read once, so a plain Response is cloned for each call and a function builds a fresh one.
export type Responder = Response | ((url: string) => Response);

const EMPTY_ROUTES: Record<ApiRoute, Responder> = {
  dashboard: jsonResponse(dashboard),
  history: jsonResponse({ range: '1Y', points: [] }),
  portfolios: jsonResponse([]),
  cash: jsonResponse(cashAccountsResponse([])),
  deposits: jsonResponse([]),
  bonds: jsonResponse([]),
  savings: jsonResponse([]),
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
