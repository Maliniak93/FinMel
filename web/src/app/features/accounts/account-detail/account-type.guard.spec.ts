import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  RouterStateSnapshot,
  Router,
  convertToParamMap,
  provideRouter,
} from '@angular/router';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import type { AccountResponse } from '../../../api/portfolio';
import { accountTypeGuard } from './account-type.guard';

const ACCOUNT_ID = '11111111-1111-1111-1111-111111111111';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function account(type: number): AccountResponse {
  return {
    id: ACCOUNT_ID,
    name: 'Account',
    type,
    wrapper: 0,
    currency: 'PLN',
    isArchived: false,
    cashAssetId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
    cashBalance: 0,
  };
}

function runGuard(path: 'accounts/:id' | 'bank/:id') {
  const url = `wealth/${path.replace(':id', ACCOUNT_ID)}`;
  const route = {
    params: { id: ACCOUNT_ID },
    paramMap: convertToParamMap({ id: ACCOUNT_ID }),
    routeConfig: { path },
    url: [],
  } as unknown as ActivatedRouteSnapshot;
  const state = { url: `/${url}` } as RouterStateSnapshot;
  return TestBed.runInInjectionContext(() => accountTypeGuard(route, state));
}

describe('accountTypeGuard', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideRouter([])] });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  it('redirects a Bank account from accounts/:id', async () => {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(account(2)));

    const result = await runGuard('accounts/:id');

    expect(result).toEqual(TestBed.inject(Router).createUrlTree(['/wealth/bank', ACCOUNT_ID]));
  });

  it('redirects a non-Bank account from bank/:id', async () => {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(account(1)));

    const result = await runGuard('bank/:id');

    expect(result).toEqual(TestBed.inject(Router).createUrlTree(['/wealth/accounts', ACCOUNT_ID]));
  });

  it('keeps a non-Bank account on accounts/:id', async () => {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(jsonResponse(account(0)));

    expect(await runGuard('accounts/:id')).toBe(true);
  });

  it('lets the navigation through when the lookup fails', async () => {
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValue(jsonResponse({ title: 'Not found', status: 404 }, 404));

    expect(await runGuard('accounts/:id')).toBe(true);
    expect(await runGuard('bank/:id')).toBe(true);
  });
});
