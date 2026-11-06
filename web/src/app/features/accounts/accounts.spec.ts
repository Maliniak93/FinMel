import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuTrigger } from '@angular/material/menu';
import { MatSnackBar } from '@angular/material/snack-bar';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { restoreEnglish, switchLanguage } from '../../../testing/i18n';
import { provideI18nTesting } from '../../core/i18n/testing';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { AccountResponse } from '../../api/portfolio';
import { client as reportingClient } from '../../api/reporting/client.gen';
import type { DashboardResponse } from '../../api/reporting';
import { formatMoney, formatPercent, formatSignedMoney } from '../../shared/format';
import { Accounts } from './accounts';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function requestUrl(input: unknown): string {
  return typeof input === 'string' ? input : (input as Request).url;
}

const brokerage: AccountResponse = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'XTB IKE',
  type: 0,
  wrapper: 1,
  currency: 'PLN',
  isArchived: false,
  cashAssetId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1',
  cashBalance: 2000,
  outsideNetWorth: false,
};

const bonds: AccountResponse = {
  id: '22222222-2222-2222-2222-222222222222',
  name: 'Obligacje IKE',
  type: 1,
  wrapper: 1,
  currency: 'PLN',
  isArchived: false,
  cashAssetId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2',
  cashBalance: 0,
  outsideNetWorth: false,
};

const archived: AccountResponse = {
  id: '33333333-3333-3333-3333-333333333333',
  name: 'Old broker',
  type: 0,
  wrapper: 0,
  currency: 'PLN',
  isArchived: true,
  cashAssetId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3',
  cashBalance: 0,
  outsideNetWorth: false,
};

const emptyDashboard: DashboardResponse = {
  netWorthPln: 0,
  asOf: null,
  isStale: false,
  byAssetClass: [],
  byAccount: [],
  byGroup: [],
  byAsset: [],
};

describe('Accounts', () => {
  let fixture: ComponentFixture<Accounts>;
  let component: Accounts;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let snackBar: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    reportingClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(
    accountsResponse: Response | Promise<Response>,
    dashboardResponse: Response = jsonResponse(emptyDashboard),
    settle = true,
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (url.includes('/api/reporting/dashboard')) {
        return dashboardResponse.clone();
      }
      return accountsResponse instanceof Promise ? accountsResponse : accountsResponse.clone();
    });
    dialog = { open: vi.fn() };
    snackBar = { open: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [Accounts],
      providers: [
        provideRouter([]),
        provideI18nTesting(),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Accounts);
    component = fixture.componentInstance;
    if (settle) {
      await fixture.whenStable();
    }
    fixture.detectChanges();
  }

  function pageText(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function accountsListReloadedFrom(index: number): boolean {
    return fetchSpy.mock.calls
      .slice(index)
      .map(([input]: [unknown]) => input as Request)
      .some(
        (request: Request) =>
          request.method === 'GET' && request.url.includes('/api/portfolio/accounts'),
      );
  }

  interface Performance {
    netContributionsPln: number | null;
    positionsCostPln: number | null;
    paidInterestPln: number | null;
  }

  function withPerformance(
    base: AccountResponse,
    overrides: Partial<AccountResponse> & Performance,
  ): AccountResponse {
    return { ...base, ...overrides } as AccountResponse;
  }

  function dashboardOf(
    entries: { id: string; valuePln: number; isStale?: boolean }[],
  ): DashboardResponse {
    const total = entries.reduce((sum, entry) => sum + entry.valuePln, 0);
    return {
      ...emptyDashboard,
      netWorthPln: total,
      asOf: '2026-08-10',
      byAccount: entries.map((entry) => ({
        accountId: entry.id,
        valuePln: entry.valuePln,
        percentage: 0,
        snapshotDate: '2026-08-10',
        isStale: entry.isStale ?? false,
      })),
    };
  }

  function rowCells(name: string): HTMLTableCellElement[] {
    const row = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('tr')).find(
      (candidate) => (candidate.textContent ?? '').includes(name),
    );
    return Array.from(row!.querySelectorAll('td'));
  }

  function normalized(text: string | null | undefined): string {
    return (text ?? '').replace(/\s+/g, ' ');
  }

  const signed = (amount: number): string => normalized(formatSignedMoney(amount));
  const pct = (percent: number): string => normalized(formatPercent(percent));
  const zero = (): string => normalized(formatMoney(0));

  function stripText(): string {
    const strip = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="summary-strip"]',
    );
    return normalized(strip?.textContent);
  }

  const eMakler = (overrides: Partial<Performance> = {}): AccountResponse =>
    withPerformance(brokerage, {
      name: 'IKE eMakler',
      cashBalance: 2000,
      netContributionsPln: 7000,
      positionsCostPln: 4000,
      paidInterestPln: 0,
      ...overrides,
    });

  const ikeBonds = (overrides: Partial<Performance> = {}): AccountResponse =>
    withPerformance(bonds, {
      name: 'IKE-Obligacje',
      cashBalance: 0,
      netContributionsPln: 5500,
      positionsCostPln: 5500,
      paidInterestPln: 50,
      ...overrides,
    });

  async function resetAndSetup(
    accounts: AccountResponse[],
    dashboard: DashboardResponse,
  ): Promise<void> {
    fetchSpy.mockRestore();
    TestBed.resetTestingModule();
    await setup(jsonResponse(accounts), jsonResponse(dashboard));
  }

  it('total gain from net contributions', async () => {
    const dashboard = dashboardOf([
      { id: brokerage.id, valuePln: 7130.68 },
      { id: bonds.id, valuePln: 5500 },
    ]);
    await setup(jsonResponse([eMakler(), ikeBonds()]), jsonResponse(dashboard));

    const emaklerValue = rowCells('IKE eMakler')[5];
    const bondsValue = rowCells('IKE-Obligacje')[5];
    expect(normalized(emaklerValue.textContent)).toContain(`${signed(130.68)}(${pct(1.87)})`);
    expect(emaklerValue.querySelector('.gain--positive')).not.toBeNull();
    expect(normalized(bondsValue.textContent)).toContain(`${zero()}(${pct(0)})`);
    expect(bondsValue.querySelector('.gain--zero')).not.toBeNull();
    expect(bondsValue.querySelector('.gain--positive, .gain--negative')).toBeNull();
    expect(stripText()).toContain(`${signed(130.68)}(${pct(1.05)})`);

    await switchLanguage(fixture, 'pl');
    expect(stripText()).toContain('od wpłat');
    const headers = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('th'),
      (th) => normalized(th.textContent).trim(),
    );
    expect(headers.find((header) => header.startsWith('Wartość'))).toContain('od wpłat');

    await restoreEnglish();
    await resetAndSetup(
      [eMakler({ netContributionsPln: 8000 })],
      dashboardOf([{ id: brokerage.id, valuePln: 7000 }]),
    );
    const lossValue = rowCells('IKE eMakler')[5];
    expect(lossValue.querySelector('.gain--negative')).not.toBeNull();
    expect(normalized(lossValue.textContent)).toContain(signed(-1000));
  });

  it('securities gain', async () => {
    const dashboard = dashboardOf([
      { id: brokerage.id, valuePln: 7130.68 },
      { id: bonds.id, valuePln: 5500 },
    ]);
    await setup(jsonResponse([eMakler(), ikeBonds()]), jsonResponse(dashboard));

    const emaklerSecurities = rowCells('IKE eMakler')[4];
    const bondsSecurities = rowCells('IKE-Obligacje')[4];
    expect(normalized(emaklerSecurities.textContent)).toContain(
      `${signed(1130.68)}(${pct(28.27)})`,
    );
    expect(normalized(bondsSecurities.textContent)).toContain(`${signed(50)}(${pct(0.91)})`);
    expect(stripText()).toContain(`${signed(1180.68)}(${pct(12.43)})`);

    await switchLanguage(fixture, 'pl');
    expect(stripText()).toContain('niezrealizowany');
    const headers = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('th'),
      (th) => normalized(th.textContent).trim(),
    );
    expect(headers.find((header) => header.startsWith('Papiery'))).toContain('niezrealizowany');
  });

  it('gain edge cases', async () => {
    await setup(
      jsonResponse([eMakler({ netContributionsPln: -500 })]),
      jsonResponse(dashboardOf([{ id: brokerage.id, valuePln: 1000 }])),
    );
    const nonPositiveContributions = normalized(rowCells('IKE eMakler')[5].textContent);
    expect(nonPositiveContributions).toContain(`${signed(1500)}(—)`);
    expect(nonPositiveContributions).not.toContain('%');

    await resetAndSetup(
      [eMakler({ netContributionsPln: 1000 })],
      dashboardOf([{ id: brokerage.id, valuePln: 0 }]),
    );
    expect(normalized(rowCells('IKE eMakler')[5].textContent)).toContain(
      `${signed(-1000)}(${pct(-100)})`,
    );

    const staleAccount = withPerformance(bonds, {
      name: 'Stale IKE',
      cashBalance: 0,
      netContributionsPln: 1000,
      positionsCostPln: 1000,
      paidInterestPln: 0,
    });
    const missingAccount = withPerformance(archived, {
      name: 'Missing IKE',
      isArchived: false,
      cashBalance: 0,
      netContributionsPln: 1000,
      positionsCostPln: 1000,
      paidInterestPln: 0,
    });
    await resetAndSetup(
      [eMakler(), staleAccount, missingAccount],
      dashboardOf([
        { id: brokerage.id, valuePln: 7130.68 },
        { id: bonds.id, valuePln: 900, isStale: true },
      ]),
    );
    expect(normalized(rowCells('Stale IKE')[5].textContent)).toContain('—');
    expect(normalized(rowCells('Stale IKE')[4].textContent)).toContain('—');
    expect(rowCells('Stale IKE')[5].querySelector('app-gain .gain:not(.gain--empty)')).toBeNull();
    expect(normalized(rowCells('Missing IKE')[5].textContent)).toContain('—');
    expect(normalized(rowCells('Missing IKE')[4].textContent)).toContain('—');
    expect(stripText()).toContain(`${signed(130.68)}(${pct(1.87)})`);
    await switchLanguage(fixture, 'pl');
    expect(stripText()).toContain('bez 2 rachunków');
    await restoreEnglish();

    await resetAndSetup(
      [eMakler({ positionsCostPln: null })],
      dashboardOf([{ id: brokerage.id, valuePln: 7130.68 }]),
    );
    expect(normalized(rowCells('IKE eMakler')[4].textContent)).toContain('—');
    expect(normalized(rowCells('IKE eMakler')[5].textContent)).toContain(signed(130.68));

    await resetAndSetup(
      [staleAccount],
      dashboardOf([{ id: bonds.id, valuePln: 900, isStale: true }]),
    );
    const strip = (fixture.nativeElement as HTMLElement).querySelector(
      '[data-testid="summary-strip"]',
    );
    expect(strip!.querySelector('app-gain .gain:not(.gain--empty)')).toBeNull();
    expect(stripText()).toContain('—');
  });

  it('loads accounts into the resource', async () => {
    await setup(jsonResponse([brokerage, bonds]));

    expect(component['accountsResource'].value()).toEqual([brokerage, bonds]);
    expect(pageText()).toContain('XTB IKE');
    expect(pageText()).toContain('Obligacje IKE');
  });

  it('summary strip and columns', async () => {
    const dashboard: DashboardResponse = {
      ...emptyDashboard,
      netWorthPln: 5000,
      asOf: '2026-08-10',
      byAccount: [
        {
          accountId: brokerage.id,
          valuePln: 5000,
          percentage: 100,
          snapshotDate: '2026-08-10',
          isStale: false,
        },
      ],
    };
    await setup(jsonResponse([brokerage, archived]), jsonResponse(dashboard));
    const element = fixture.nativeElement as HTMLElement;
    const strip = element.querySelector<HTMLElement>('[data-testid="summary-strip"]');

    expect(element.querySelector('h1')).toBeNull();
    expect(strip).not.toBeNull();
    const stripText = (strip!.textContent ?? '').replace(/\s+/g, ' ');
    expect(stripText).toContain('Total');
    expect(stripText).toContain('Free cash');
    expect(stripText).toContain('Securities');
    expect(stripText).toContain(formatMoney(5000).replace(/\s+/g, ' '));
    expect(stripText).toContain(formatMoney(2000).replace(/\s+/g, ' '));
    expect(stripText).toContain(formatMoney(3000).replace(/\s+/g, ' '));

    await switchLanguage(fixture, 'pl');
    const headers = Array.from(element.querySelectorAll('th'), (th) =>
      normalized(th.textContent).trim(),
    );
    expect(headers.slice(0, 6)).toEqual([
      'Nazwa',
      'Typ',
      'Opakowanie podatkowe',
      'Wolne środki',
      'Papiery niezrealizowany',
      'Wartość od wpłat',
    ]);
  });

  it('archived rows', async () => {
    await setup(jsonResponse([brokerage, archived]));
    component['includeArchived'].set(true);
    await fixture.whenStable();
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    const archivedRow = element.querySelector<HTMLElement>('tr[data-archived="true"]');
    expect(archivedRow?.textContent).toContain('Old broker');
    expect(archivedRow?.querySelector('app-status-chip')?.textContent).toContain('Archived');
    expect(element.querySelectorAll('tr[data-archived="true"]').length).toBe(1);

    await switchLanguage(fixture, 'pl');
    expect(archivedRow?.querySelector('app-status-chip')?.textContent).toContain('Zarchiwizowany');
  });

  it('states', async () => {
    await setup(new Promise<Response>(() => undefined), jsonResponse(emptyDashboard), false);
    expect((fixture.nativeElement as HTMLElement).querySelector('app-skeleton')).not.toBeNull();
    fetchSpy.mockRestore();
    TestBed.resetTestingModule();

    await setup(jsonResponse({ detail: 'Boom.' }, 500));
    const errorBlock = (fixture.nativeElement as HTMLElement).querySelector('app-section-error');
    expect(errorBlock).not.toBeNull();
    expect(errorBlock?.textContent).toContain('Retry');
    await switchLanguage(fixture, 'pl');
    expect(errorBlock?.textContent).toContain('Ponów');
    fetchSpy.mockRestore();
    await restoreEnglish();
    TestBed.resetTestingModule();

    await setup(jsonResponse([]));
    const empty = (fixture.nativeElement as HTMLElement).querySelector('app-empty-state');
    expect(empty).not.toBeNull();
    expect(empty?.querySelector('button')?.textContent).toContain('New account');
  });

  it('shows the empty state when only bank accounts exist', async () => {
    const bank: AccountResponse = {
      ...brokerage,
      id: '44444444-4444-4444-4444-444444444444',
      name: 'mBank',
      type: 2,
    };
    await setup(jsonResponse([bank]));
    const element = fixture.nativeElement as HTMLElement;

    expect(pageText()).toContain("You don't have any accounts yet.");
    expect(pageText()).not.toContain('mBank');
    expect(element.querySelector('table')).toBeNull();
  });

  it('shows archived rows only with the toggle on', async () => {
    await setup(jsonResponse([brokerage, archived]));

    expect(pageText()).not.toContain('Old broker');

    component['includeArchived'].set(true);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(pageText()).toContain('Old broker');
  });

  it('renders the value from byAccount', async () => {
    const dashboard: DashboardResponse = {
      ...emptyDashboard,
      netWorthPln: 12345.67,
      asOf: '2026-08-10',
      byAccount: [
        {
          accountId: brokerage.id,
          valuePln: 12345.67,
          percentage: 100,
          snapshotDate: '2026-08-10',
          isStale: false,
        },
      ],
    };
    await setup(jsonResponse([brokerage]), jsonResponse(dashboard));

    expect(pageText()).toContain(formatMoney(12345.67));
  });

  it('links each row to its account page', async () => {
    await setup(jsonResponse([brokerage, bonds]));
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector(`a[href="/wealth/accounts/${brokerage.id}"]`)).not.toBeNull();
    expect(element.querySelector(`a[href="/wealth/accounts/${bonds.id}"]`)).not.toBeNull();
  });

  it('shows the cash from the account and the securities as the total minus the cash', async () => {
    const dashboard: DashboardResponse = {
      ...emptyDashboard,
      netWorthPln: 5000,
      asOf: '2026-08-10',
      byAccount: [
        {
          accountId: brokerage.id,
          valuePln: 5000,
          percentage: 100,
          snapshotDate: '2026-08-10',
          isStale: false,
        },
      ],
    };
    await setup(jsonResponse([brokerage]), jsonResponse(dashboard));

    expect(pageText()).toContain(formatMoney(2000));
    expect(pageText()).toContain(formatMoney(3000));
    expect(pageText()).toContain(formatMoney(5000));
  });

  it('labels the account cash column Free funds, and Wolne środki in Polish', async () => {
    await setup(jsonResponse([brokerage]));

    expect(pageText()).toContain('Free funds');

    await switchLanguage(fixture, 'pl');

    expect(pageText()).toContain('Wolne środki');
  });

  it('reloads after a successful create-dialog save', async () => {
    await setup(jsonResponse([brokerage]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;

    component['openCreateDialog']();
    await fixture.whenStable();

    expect(accountsListReloadedFrom(callsBefore)).toBe(true);
  });

  it('opens the edit dialog with the account and reloads after a save', async () => {
    await setup(jsonResponse([brokerage]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;

    component['openEditDialog'](brokerage);
    await fixture.whenStable();

    expect(dialog.open.mock.calls[0][1].data).toEqual({ account: brokerage });
    expect(accountsListReloadedFrom(callsBefore)).toBe(true);
  });

  it('archives an account after confirmation and reloads', async () => {
    await setup(jsonResponse([brokerage]));
    const callsBefore = fetchSpy.mock.calls.length;
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    fetchSpy.mockResolvedValueOnce(jsonResponse({ ...brokerage, isArchived: true }));

    await component['archive'](brokerage);
    await fixture.whenStable();

    const archiveCall = fetchSpy.mock.calls[callsBefore][0] as Request;
    expect(archiveCall.method).toBe('POST');
    expect(archiveCall.url).toContain(`/accounts/${brokerage.id}/archive`);
    expect(accountsListReloadedFrom(callsBefore + 1)).toBe(true);
  });

  it('shows a snackbar and does not reload when archiving a non-empty account', async () => {
    await setup(jsonResponse([brokerage]));
    const callsBefore = fetchSpy.mock.calls.length;
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    fetchSpy.mockResolvedValueOnce(
      jsonResponse(
        { detail: 'The account still holds assets.', errorCode: 'Conflict.AccountNotEmpty' },
        409,
      ),
    );

    await component['archive'](brokerage);
    await fixture.whenStable();

    expect(snackBar.open).toHaveBeenCalledTimes(1);
    expect(snackBar.open.mock.calls[0][0]).toBeTruthy();
    expect(fetchSpy).toHaveBeenCalledTimes(callsBefore + 1);
  });

  it('restores an archived account after confirmation and reloads', async () => {
    await setup(jsonResponse([archived]));
    component['includeArchived'].set(true);
    await fixture.whenStable();
    const callsBefore = fetchSpy.mock.calls.length;
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    fetchSpy.mockResolvedValueOnce(jsonResponse({ ...archived, isArchived: false }));

    await component['restore'](archived);
    await fixture.whenStable();

    const restoreCall = fetchSpy.mock.calls[callsBefore][0] as Request;
    expect(restoreCall.method).toBe('POST');
    expect(restoreCall.url).toContain(`/accounts/${archived.id}/restore`);
    expect(accountsListReloadedFrom(callsBefore + 1)).toBe(true);
  });

  it('shows Restore only for archived accounts in the row menu', async () => {
    await setup(jsonResponse([archived]));
    component['includeArchived'].set(true);
    await fixture.whenStable();
    fixture.detectChanges();

    const overlayContainer = TestBed.inject(OverlayContainer);
    const trigger = fixture.debugElement
      .query(By.directive(MatMenuTrigger))
      .injector.get(MatMenuTrigger);
    trigger.openMenu();
    fixture.detectChanges();
    await fixture.whenStable();

    const menuText = overlayContainer.getContainerElement().textContent ?? '';
    expect(menuText).toContain('Restore');
    expect(menuText).not.toContain('Archive');
  });

  it('deletes an empty account after confirmation and reloads', async () => {
    await setup(jsonResponse([brokerage]));
    const callsBefore = fetchSpy.mock.calls.length;
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    fetchSpy.mockResolvedValueOnce(new Response(null, { status: 204 }));

    await component['remove'](brokerage);
    await fixture.whenStable();

    const deleteCall = fetchSpy.mock.calls[callsBefore][0] as Request;
    expect(deleteCall.method).toBe('DELETE');
    expect(deleteCall.url).toContain(`/accounts/${brokerage.id}`);
    expect(accountsListReloadedFrom(callsBefore + 1)).toBe(true);
  });

  it('shows a snackbar and does not reload when deleting an account with history', async () => {
    await setup(jsonResponse([brokerage]));
    const callsBefore = fetchSpy.mock.calls.length;
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    fetchSpy.mockResolvedValueOnce(
      jsonResponse(
        { detail: 'The account has assets.', errorCode: 'Conflict.AccountHasHistory' },
        409,
      ),
    );

    await component['remove'](brokerage);
    await fixture.whenStable();

    expect(snackBar.open).toHaveBeenCalledTimes(1);
    expect(snackBar.open.mock.calls[0][0]).toBeTruthy();
    expect(fetchSpy).toHaveBeenCalledTimes(callsBefore + 1);
  });
});
