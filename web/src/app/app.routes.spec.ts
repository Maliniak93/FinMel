import { Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { App } from './app';
import { routes } from './app.routes';
import { AuthService } from './core/auth/auth';
import { Login } from './features/auth/login/login';
import { BankAccounts } from './features/bank-accounts/bank-accounts';
import { Securities } from './features/securities/securities';
import { WealthLayout } from './features/wealth/wealth-layout';
import { Bonds } from './features/bonds/bonds';
import { Cash } from './features/cash/cash';
import { Dashboard } from './features/dashboard/dashboard';
import { Deposits } from './features/deposits/deposits';
import { Metals } from './features/metals/metals';
import { NotFound } from './features/not-found/not-found';
import { Accounts } from './features/accounts/accounts';
import { AccountDetail } from './features/accounts/account-detail/account-detail';
import { client as portfolioClient } from './api/portfolio/client.gen';
import { Settings } from './features/settings/settings';
import { Shell } from './layout/shell/shell';
import { provideI18nTesting } from './core/i18n/testing';

describe('app routing (authenticated)', () => {
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter(routes),
        provideI18nTesting(),
        { provide: AuthService, useValue: { isAuthenticated: () => true } },
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  it('redirects the empty path to /dashboard', async () => {
    await harness.navigateByUrl('/', Shell);
    expect(harness.fixture.debugElement.query(By.directive(Dashboard))).toBeTruthy();
  });

  it('wealth routes: every type renders inside the layout at /wealth/<tab>', async () => {
    const tabs: [string, Type<unknown>][] = [
      ['accounts', Accounts],
      ['bank', BankAccounts],
      ['cash', Cash],
      ['deposits', Deposits],
      ['bonds', Bonds],
      ['securities', Securities],
      ['metals', Metals],
    ];
    for (const [path, component] of tabs) {
      await harness.navigateByUrl(`/wealth/${path}`, Shell);
      const element = harness.fixture.debugElement;
      expect(element.query(By.directive(WealthLayout)), path).toBeTruthy();
      expect(element.query(By.directive(component)), path).toBeTruthy();
    }
  });

  it('wealth routes: /wealth redirects to the accounts tab', async () => {
    await harness.navigateByUrl('/wealth', Shell);
    expect(TestBed.inject(Router).url).toBe('/wealth/accounts');
    expect(harness.fixture.debugElement.query(By.directive(Accounts))).toBeTruthy();
  });

  it('wealth routes: detail pages render without the tab bar', async () => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    const bankId = '11111111-1111-1111-1111-111111111111';
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(
      async () =>
        new Response(
          JSON.stringify({
            id: bankId,
            name: 'mBank',
            type: 2,
            wrapper: 0,
            currency: 'PLN',
            isArchived: false,
            cashAssetId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
            cashBalance: 0,
          }),
          { status: 200, headers: { 'Content-Type': 'application/json' } },
        ),
    );

    await harness.navigateByUrl(`/wealth/bank/${bankId}`, Shell);
    fetchSpy.mockRestore();

    const element = harness.fixture.debugElement;
    expect(element.query(By.directive(AccountDetail))).toBeTruthy();
    expect(element.query(By.directive(WealthLayout))).toBeNull();
  });

  it('old routes are gone: old addresses and an unknown wealth tab show the 404 page', async () => {
    const oldPaths = [
      'accounts',
      'bank-accounts/11111111-1111-1111-1111-111111111111',
      'cash',
      'assets/a1/transactions',
    ];
    for (const url of [...oldPaths.map((path) => `/${path}`), '/wealth/xyz']) {
      await harness.navigateByUrl(url, Shell);
      expect(harness.fixture.debugElement.query(By.directive(NotFound)), url).toBeTruthy();
      expect(TestBed.inject(Router).url, url).toBe(url);
    }
  });

  it('renders Settings at /settings', async () => {
    await harness.navigateByUrl('/settings', Shell);
    expect(harness.fixture.debugElement.query(By.directive(Settings))).toBeTruthy();
  });

  it('renders the 404 page for unknown routes', async () => {
    await harness.navigateByUrl('/nope', Shell);
    expect(harness.fixture.debugElement.query(By.directive(NotFound))).toBeTruthy();
  });

  it('redirects away from /login back to /dashboard', async () => {
    await harness.navigateByUrl('/login', Shell);
    expect(harness.fixture.debugElement.query(By.directive(Dashboard))).toBeTruthy();
  });
});

describe('app routing (unauthenticated)', () => {
  let harness: RouterTestingHarness;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter(routes),
        provideI18nTesting(),
        { provide: AuthService, useValue: { isAuthenticated: () => false } },
      ],
    });
    harness = await RouterTestingHarness.create();
  });

  it('renders Login at /login', async () => {
    const component = await harness.navigateByUrl('/login', Login);
    expect(component).toBeInstanceOf(Login);
  });

  it('redirects protected routes to /login', async () => {
    const component = await harness.navigateByUrl('/dashboard', Login);
    expect(component).toBeInstanceOf(Login);
  });
});
