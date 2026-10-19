import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { App } from './app';
import { routes } from './app.routes';
import { AuthService } from './core/auth/auth';
import { Allocation } from './features/allocation/allocation';
import { Login } from './features/auth/login/login';
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

  it('renders Accounts at /accounts', async () => {
    await harness.navigateByUrl('/accounts', Shell);
    expect(harness.fixture.debugElement.query(By.directive(Accounts))).toBeTruthy();
  });

  it('renders AccountDetail at bank-accounts/:id', async () => {
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

    await harness.navigateByUrl(`/bank-accounts/${bankId}`, Shell);
    fetchSpy.mockRestore();

    expect(harness.fixture.debugElement.query(By.directive(AccountDetail))).toBeTruthy();
  });

  it('renders Allocation at /allocation', async () => {
    await harness.navigateByUrl('/allocation', Shell);
    expect(harness.fixture.debugElement.query(By.directive(Allocation))).toBeTruthy();
  });

  it('renders Cash at /cash', async () => {
    await harness.navigateByUrl('/cash', Shell);
    expect(harness.fixture.debugElement.query(By.directive(Cash))).toBeTruthy();
  });

  it('renders Deposits at /deposits', async () => {
    await harness.navigateByUrl('/deposits', Shell);
    expect(harness.fixture.debugElement.query(By.directive(Deposits))).toBeTruthy();
  });

  it('renders Bonds at /bonds', async () => {
    await harness.navigateByUrl('/bonds', Shell);
    expect(harness.fixture.debugElement.query(By.directive(Bonds))).toBeTruthy();
  });

  it('renders Metals at /metals', async () => {
    await harness.navigateByUrl('/metals', Shell);
    expect(harness.fixture.debugElement.query(By.directive(Metals))).toBeTruthy();
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
