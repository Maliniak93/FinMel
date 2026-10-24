import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTooltip } from '@angular/material/tooltip';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { CashPlacesResponse } from '../../api/portfolio';
import { provideI18nTesting } from '../../core/i18n/testing';
import { groupColor } from '../../shared/colors';
import { formatMoney } from '../../shared/format';
import {
  clickRowMenuItem,
  menuItemLabel,
  rowMenuItems,
  showArchived,
} from '../../../testing/archive';
import { restoreEnglish, switchLanguage, textOf } from '../../../testing/i18n';
import { hasSwatch } from '../../../testing/swatch';
import { CashOperationDialog } from '../accounts/cash-operation-dialog/cash-operation-dialog';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
import { assetGroups, blueGroupId, isGroupListRequest } from '../groups/testing/group-fixtures';
import { Cash } from './cash';
import { CashBankTransferDialog } from './cash-bank-transfer-dialog/cash-bank-transfer-dialog';
import { CashPlaceFormDialog } from './cash-place-form-dialog/cash-place-form-dialog';

const walletId = 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee';
const safeId = 'dddddddd-dddd-dddd-dddd-dddddddddddd';
const jarId = 'ffffffff-ffff-ffff-ffff-ffffffffffff';
const walletPlnId = '44444444-4444-4444-4444-444444444444';
const safePlnId = '11111111-1111-1111-1111-111111111111';
const safeEurId = '22222222-2222-2222-2222-222222222222';
const safeChfId = '33333333-3333-3333-3333-333333333333';
const jarPlnId = '55555555-5555-5555-5555-555555555555';

const populated: CashPlacesResponse = {
  places: [
    {
      id: walletId,
      name: 'Wallet',
      isArchived: false,
      groupId: blueGroupId,
      valuePln: 800,
      balances: [{ currency: 'PLN', cashAssetId: walletPlnId, balance: 800, valuePln: 800 }],
    },
    {
      id: safeId,
      name: 'Safe',
      isArchived: false,
      groupId: null,
      valuePln: null,
      balances: [
        { currency: 'PLN', cashAssetId: safePlnId, balance: 3500, valuePln: 3500 },
        { currency: 'EUR', cashAssetId: safeEurId, balance: 420, valuePln: 1806 },
        { currency: 'CHF', cashAssetId: safeChfId, balance: 200, valuePln: null },
      ],
    },
    {
      id: jarId,
      name: 'Jar',
      isArchived: true,
      groupId: null,
      valuePln: 0,
      balances: [{ currency: 'PLN', cashAssetId: jarPlnId, balance: 0, valuePln: 0 }],
    },
  ],
  totals: [
    { currency: 'PLN', balance: 4300 },
    { currency: 'EUR', balance: 420 },
    { currency: 'CHF', balance: 200 },
  ],
  totalValuePln: 6106,
  fxUnavailable: true,
};

function spaced(text: string): string {
  return text.replace(/\s+/g, ' ');
}

function isDisabled(item: Element): boolean {
  return item.hasAttribute('disabled') || item.getAttribute('aria-disabled') === 'true';
}

describe('Cash', () => {
  let fixture: ComponentFixture<Cash>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let failNextLoad: boolean;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let snackBar: { open: ReturnType<typeof vi.fn> };
  let afterClosed: unknown;
  let writeResponse: () => Response;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(
    body: CashPlacesResponse = populated,
    fail = false,
    pending = false,
  ): Promise<void> {
    failNextLoad = fail;
    afterClosed = true;
    writeResponse = () => new Response(null, { status: 204 });
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      if (isGroupListRequest(input)) {
        return jsonResponse(assetGroups);
      }
      if (!requestUrl(input).includes('/api/portfolio/cash-places')) {
        return jsonResponse({ detail: 'Not found.' }, 404);
      }
      if (request.method !== 'GET') {
        return writeResponse();
      }
      if (pending) {
        return new Promise<Response>(() => undefined);
      }
      if (failNextLoad) {
        failNextLoad = false;
        return jsonResponse({ detail: 'Cash is unavailable.' }, 500);
      }
      return jsonResponse(body);
    });

    dialog = { open: vi.fn().mockImplementation(() => ({ afterClosed: () => of(afterClosed) })) };
    snackBar = { open: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [Cash],
      providers: [
        provideI18nTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Cash);
    if (!pending) {
      await fixture.whenStable();
    }
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function placeCard(name: string): HTMLElement {
    const card = Array.from(element().querySelectorAll<HTMLElement>('mat-card.cash-place')).find(
      (c) => (c.querySelector('.cash-place__name')?.textContent ?? '').trim() === name,
    );
    if (!card) {
      throw new Error(`No card for '${name}'.`);
    }
    return card;
  }

  function placeHeader(name: string): HTMLElement {
    return placeCard(name).querySelector<HTMLElement>('.cash-place__header')!;
  }

  function balanceRow(place: string, currency: string): HTMLElement {
    const row = Array.from(
      placeCard(place).querySelectorAll<HTMLElement>('li.cash-place__balance'),
    ).find(
      (li) => (li.querySelector('.cash-place__currency')?.textContent ?? '').trim() === currency,
    );
    if (!row) {
      throw new Error(`No ${currency} balance in '${place}'.`);
    }
    return row;
  }

  function listCalls(): number {
    return fetchSpy.mock.calls.filter(
      (call: unknown[]) =>
        (call[0] as Request).method === 'GET' &&
        requestUrl(call[0]).includes('/api/portfolio/cash-places'),
    ).length;
  }

  function writes(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((request: Request) => request.method !== 'GET');
  }

  it('totals strip and rows', async () => {
    await setup();
    const strip = element().querySelector<HTMLElement>('.cash-page__totals')!;

    expect(textOf(strip)).toContain(spaced(formatMoney(6106)));
    expect(Array.from(strip.querySelectorAll('.cash-page__total'), textOf)).toEqual([
      `PLN: ${spaced(formatMoney(4300, 'PLN'))}`,
      `EUR: ${spaced(formatMoney(420, 'EUR'))}`,
      `CHF: ${spaced(formatMoney(200, 'CHF'))}`,
    ]);
    expect(strip.querySelector('.status-chip--uncertain')?.textContent).toContain(
      'Exchange rate unavailable',
    );
    expect(
      fixture.debugElement
        .queryAll(By.directive(MatTooltip))
        .map((debug) => debug.injector.get(MatTooltip).message),
    ).toContain(`No rate for CHF — the total excludes ${formatMoney(200, 'CHF')}`);
    expect(textOf(balanceRow('Safe', 'CHF'))).toContain('Exchange rate unavailable');
    expect(textOf(balanceRow('Safe', 'EUR'))).toContain('≈');
    expect(textOf(balanceRow('Safe', 'EUR'))).toContain(spaced(formatMoney(1806)));

    await switchLanguage(fixture, 'pl');
    expect(textOf(element().querySelector('.cash-page__totals'))).toContain('Łączna wartość');
  });

  it('operations menu', async () => {
    await setup();

    const items = await rowMenuItems(fixture, balanceRow('Safe', 'EUR'));
    const sequence = Array.from(items[0].parentElement!.children, (child) =>
      child.getAttribute('role') === 'separator' ? 'separator' : menuItemLabel(child),
    );

    expect(sequence).toEqual([
      'Deposit',
      'Withdraw',
      'separator',
      'ATM withdrawal',
      'Deposit to bank',
      'separator',
      'History',
    ]);
  });

  it('default group', async () => {
    await setup();

    expect(textOf(placeCard('Wallet'))).toContain('Default group: Emerytura');
    expect(hasSwatch(placeCard('Wallet'), groupColor('blue'))).toBe(true);
    expect(textOf(placeCard('Safe'))).not.toContain('Default group');

    await switchLanguage(fixture, 'pl');
    expect(textOf(placeCard('Wallet'))).toContain('Domyślna grupa: Emerytura');
  });

  it('archived place', async () => {
    await setup();
    expect(element().querySelectorAll('mat-card.cash-place')).toHaveLength(2);

    await showArchived(fixture);

    const jar = placeCard('Jar');
    expect(element().querySelectorAll('mat-card.cash-place')).toHaveLength(3);
    expect(jar.classList).toContain('cash-place--archived');
    expect(jar.querySelector('app-status-chip')?.textContent).toContain('Archived');

    const items = await rowMenuItems(fixture, balanceRow('Jar', 'PLN'));
    for (const label of [/^deposit$/i, /^withdraw$/i, /^atm withdrawal$/i, /^deposit to bank$/i]) {
      const item = items.find((candidate) => label.test(menuItemLabel(candidate)));
      expect(item, String(label)).toBeDefined();
      expect(isDisabled(item!), String(label)).toBe(true);
    }
  });

  it('states', async () => {
    await setup(populated, false, true);
    expect(element().querySelector('app-skeleton')).not.toBeNull();
    fetchSpy.mockRestore();
    TestBed.resetTestingModule();

    await setup(populated, true);
    const errorBlock = element().querySelector<HTMLElement>('app-section-error');
    expect(errorBlock?.textContent).toContain('Retry');
    await switchLanguage(fixture, 'pl');
    expect(errorBlock?.textContent).toContain('Ponów');
    errorBlock!.querySelector<HTMLButtonElement>('button')!.click();
    await fixture.whenStable();
    expect(element().querySelectorAll('mat-card.cash-place')).toHaveLength(2);
    fetchSpy.mockRestore();
    await restoreEnglish();
    TestBed.resetTestingModule();

    await setup({ places: [], totals: [], totalValuePln: 0, fxUnavailable: false });
    const empty = element().querySelector<HTMLElement>('app-empty-state');
    expect(empty?.textContent).toContain('No cash places yet');
    expect(empty?.querySelector('button')?.textContent).toContain('Add place');
    expect(element().querySelector('mat-card.cash-place')).toBeNull();
  });

  it('adds a place through the form dialog and reloads', async () => {
    await setup();
    const before = listCalls();

    Array.from(element().querySelectorAll<HTMLButtonElement>('button'))
      .find((b) => /add place/i.test(b.textContent ?? ''))!
      .click();
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledWith(
      CashPlaceFormDialog,
      expect.objectContaining({ data: { mode: 'create' } }),
    );
    await vi.waitFor(() => expect(listCalls()).toBeGreaterThan(before));
  });

  it('renames a place and adds a currency through the form dialog', async () => {
    await setup();

    await clickRowMenuItem(fixture, placeHeader('Safe'), /^rename$/i);
    expect(dialog.open).toHaveBeenLastCalledWith(
      CashPlaceFormDialog,
      expect.objectContaining({
        data: { mode: 'rename', place: expect.objectContaining({ id: safeId }) },
      }),
    );

    await clickRowMenuItem(fixture, placeHeader('Safe'), /^add currency$/i);
    expect(dialog.open).toHaveBeenLastCalledWith(
      CashPlaceFormDialog,
      expect.objectContaining({
        data: { mode: 'addCurrency', place: expect.objectContaining({ id: safeId }) },
      }),
    );
  });

  it('opens a deposit or withdrawal for the balance and reloads', async () => {
    await setup();

    for (const [label, kind] of [
      [/^deposit$/i, 'deposit'],
      [/^withdraw$/i, 'withdraw'],
    ] as const) {
      dialog.open.mockClear();
      const before = listCalls();

      await clickRowMenuItem(fixture, balanceRow('Safe', 'EUR'), label);

      expect(dialog.open).toHaveBeenCalledWith(
        CashOperationDialog,
        expect.objectContaining({ data: { cashAssetId: safeEurId, kind } }),
      );
      await vi.waitFor(() => expect(listCalls()).toBeGreaterThan(before));
    }
  });

  it('opens the ATM withdrawal and the bank deposit anchored on the balance', async () => {
    await setup();

    for (const [label, direction] of [
      [/^atm withdrawal$/i, 'atm'],
      [/^deposit to bank$/i, 'bankDeposit'],
    ] as const) {
      dialog.open.mockClear();

      await clickRowMenuItem(fixture, balanceRow('Safe', 'PLN'), label);

      expect(dialog.open).toHaveBeenCalledWith(
        CashBankTransferDialog,
        expect.objectContaining({
          data: {
            direction,
            anchor: 'place',
            anchorAssetId: safePlnId,
            anchorName: 'Safe · PLN',
            currency: 'PLN',
            anchorBalance: 3500,
          },
        }),
      );
    }
  });

  it('links each balance to its transactions', async () => {
    await setup();

    const items = await rowMenuItems(fixture, balanceRow('Safe', 'EUR'));

    const link = items.find((item) => /^history$/i.test(menuItemLabel(item)));
    expect(link?.getAttribute('href')).toBe(`/wealth/assets/${safeEurId}/transactions`);
  });

  it('archives a place after confirming and reloads', async () => {
    await setup();
    const before = listCalls();

    await clickRowMenuItem(fixture, placeHeader('Safe'), /^archive$/i);

    expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
    await vi.waitFor(() => expect(writes()).toHaveLength(1));
    expect(writes()[0].method).toBe('POST');
    expect(writes()[0].url).toContain(`/api/portfolio/cash-places/${safeId}/archive`);
    await vi.waitFor(() => expect(listCalls()).toBeGreaterThan(before));
  });

  it('restores an archived place after confirming and reloads', async () => {
    await setup();
    await showArchived(fixture);
    const before = listCalls();

    await clickRowMenuItem(fixture, placeHeader('Jar'), /^restore$/i);

    expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
    await vi.waitFor(() => expect(writes()).toHaveLength(1));
    expect(writes()[0].method).toBe('POST');
    expect(writes()[0].url).toContain(`/api/portfolio/cash-places/${jarId}/restore`);
    await vi.waitFor(() => expect(listCalls()).toBeGreaterThan(before));
  });

  it('deletes a place after confirming and reloads', async () => {
    await setup();
    const before = listCalls();

    await clickRowMenuItem(fixture, placeHeader('Wallet'), /^delete$/i);

    expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
    await vi.waitFor(() => expect(writes()).toHaveLength(1));
    expect(writes()[0].method).toBe('DELETE');
    expect(writes()[0].url).toContain(`/api/portfolio/cash-places/${walletId}`);
    await vi.waitFor(() => expect(listCalls()).toBeGreaterThan(before));
  });

  it('does nothing when the confirmation is declined', async () => {
    await setup();
    afterClosed = false;

    await clickRowMenuItem(fixture, placeHeader('Safe'), /^delete$/i);

    expect(writes()).toEqual([]);
  });

  it.each([
    ['Conflict.CashPlaceNotEmpty', /not empty/i, /^archive$/i],
    ['Conflict.CashPlaceHasHistory', /has transactions/i, /^delete$/i],
  ])('shows %s as a snackbar text', async (errorCode, message, action) => {
    await setup();
    writeResponse = () =>
      jsonResponse({ title: 'Conflict', status: 409, detail: 'Server text.', errorCode }, 409);

    await clickRowMenuItem(fixture, placeHeader('Safe'), action);

    await vi.waitFor(() => expect(snackBar.open).toHaveBeenCalled());
    expect(snackBar.open.mock.calls[0][0]).toMatch(message);
  });
});
