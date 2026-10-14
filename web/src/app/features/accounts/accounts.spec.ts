import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuTrigger } from '@angular/material/menu';
import { MatSnackBar } from '@angular/material/snack-bar';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { restoreEnglish } from '../../../testing/i18n';
import { provideI18nTesting } from '../../core/i18n/testing';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { AccountResponse } from '../../api/portfolio';
import { client as reportingClient } from '../../api/reporting/client.gen';
import type { DashboardResponse } from '../../api/reporting';
import { formatMoney } from '../../shared/format';
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
};

const emptyDashboard: DashboardResponse = {
  netWorthPln: 0,
  asOf: null,
  isStale: false,
  byAssetClass: [],
  byAccount: [],
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
    accountsResponse: Response,
    dashboardResponse: Response = jsonResponse(emptyDashboard),
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      return (
        url.includes('/api/reporting/dashboard') ? dashboardResponse : accountsResponse
      ).clone();
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
    await fixture.whenStable();
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

  it('loads accounts into the resource', async () => {
    await setup(jsonResponse([brokerage, bonds]));

    expect(component['accountsResource'].value()).toEqual([brokerage, bonds]);
    expect(pageText()).toContain('XTB IKE');
    expect(pageText()).toContain('Obligacje IKE');
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
          cashPln: 0,
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

    expect(element.querySelector(`a[href="/accounts/${brokerage.id}"]`)).not.toBeNull();
    expect(element.querySelector(`a[href="/accounts/${bonds.id}"]`)).not.toBeNull();
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
          cashPln: 2000,
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
