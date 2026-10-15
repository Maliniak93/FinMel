import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { CashPlacesResponse } from '../../api/portfolio';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatMoney } from '../../shared/format';
import {
  clickRowMenuItem,
  menuItemLabel,
  rowMenuItems,
  showArchived,
} from '../../../testing/archive';
import { CashOperationDialog } from '../accounts/cash-operation-dialog/cash-operation-dialog';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
import { Cash } from './cash';
import { CashBankTransferDialog } from './cash-bank-transfer-dialog/cash-bank-transfer-dialog';
import { CashPlaceFormDialog } from './cash-place-form-dialog/cash-place-form-dialog';

const plnBalanceId = '11111111-1111-1111-1111-111111111111';
const usdBalanceId = '22222222-2222-2222-2222-222222222222';
const safeId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const jarId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

const populated: CashPlacesResponse = {
  places: [
    {
      id: safeId,
      name: 'Safe',
      isArchived: false,
      valuePln: 3200,
      balances: [
        { currency: 'PLN', cashAssetId: plnBalanceId, balance: 2000, valuePln: 2000 },
        { currency: 'USD', cashAssetId: usdBalanceId, balance: 300, valuePln: 1200 },
      ],
    },
    {
      id: jarId,
      name: 'Jar',
      isArchived: true,
      valuePln: 0,
      balances: [
        {
          currency: 'PLN',
          cashAssetId: '33333333-3333-3333-3333-333333333333',
          balance: 0,
          valuePln: 0,
        },
      ],
    },
  ],
  totals: [
    { currency: 'PLN', balance: 2000 },
    { currency: 'USD', balance: 300 },
  ],
  totalValuePln: 3200,
  fxUnavailable: false,
};

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

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(body: CashPlacesResponse = populated, fail = false): Promise<void> {
    failNextLoad = fail;
    afterClosed = true;
    writeResponse = () => new Response(null, { status: 204 });
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      if (!requestUrl(input).includes('/api/portfolio/cash-places')) {
        return jsonResponse({ detail: 'Not found.' }, 404);
      }
      if (request.method !== 'GET') {
        return writeResponse();
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
    await fixture.whenStable();
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

  it('lists the unarchived places with balances, PLN values and the totals above them', async () => {
    await setup();

    expect(element().querySelectorAll('mat-card.cash-place')).toHaveLength(1);
    const safe = placeCard('Safe').textContent ?? '';
    expect(safe).toContain(formatMoney(2000, 'PLN'));
    expect(safe).toContain(formatMoney(300, 'USD'));
    expect(safe).toContain(formatMoney(1200));
    expect(placeCard('Safe').querySelector('.cash-place__value')?.textContent).toContain(
      formatMoney(3200),
    );

    const totals = Array.from(element().querySelectorAll<HTMLElement>('.cash-page__total')).map(
      (line) => (line.textContent ?? '').trim(),
    );
    expect(totals).toEqual([`PLN: ${formatMoney(2000, 'PLN')}`, `USD: ${formatMoney(300, 'USD')}`]);
    expect(element().querySelector('.cash-page__total-value')?.textContent).toContain(
      formatMoney(3200),
    );
    expect(element().querySelector('.cash-page__fx-unavailable')).toBeNull();
    expect(element().querySelector('mat-spinner')).toBeNull();
  });

  it('shows archived places only when asked', async () => {
    await setup();

    await showArchived(fixture);

    expect(element().querySelectorAll('mat-card.cash-place')).toHaveLength(2);
    expect(placeCard('Jar').textContent).toContain('Archived');
  });

  it('flags an unavailable rate and leaves the foreign value out', async () => {
    await setup({
      ...populated,
      places: [
        {
          ...populated.places[0],
          valuePln: null,
          balances: [
            populated.places[0].balances[0],
            { ...populated.places[0].balances[1], valuePln: null },
          ],
        },
      ],
      totalValuePln: null,
      fxUnavailable: true,
    });

    expect(element().querySelector('.cash-page__fx-unavailable')?.textContent).toContain(
      'Exchange rate unavailable',
    );
    expect(element().querySelector('.cash-page__total-value')).toBeNull();
    expect(placeCard('Safe').querySelector('.cash-place__value')).toBeNull();
    expect(
      balanceRow('Safe', 'USD').querySelector('.cash-place__amount-pln')?.textContent?.trim(),
    ).toBe('—');
  });

  it('shows the empty state', async () => {
    await setup({ places: [], totals: [], totalValuePln: 0, fxUnavailable: false });

    expect(element().querySelectorAll('mat-card.cash-place')).toHaveLength(0);
    expect(element().querySelector('p')?.textContent).toContain('No cash places yet');
    expect(element().querySelector('[role=alert]')).toBeNull();
  });

  it('shows the load error with retry', async () => {
    await setup(populated, true);

    const alert = element().querySelector<HTMLElement>('[role=alert]');
    expect(alert?.textContent).toContain('Cash is unavailable.');
    const retry = Array.from(alert!.querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      (b.textContent ?? '').includes('Retry'),
    );
    expect(listCalls()).toBe(1);

    retry!.click();
    await fixture.whenStable();

    expect(listCalls()).toBe(2);
    expect(element().querySelector('[role=alert]')).toBeNull();
    expect(element().querySelectorAll('mat-card.cash-place')).toHaveLength(1);
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

      await clickRowMenuItem(fixture, balanceRow('Safe', 'USD'), label);

      expect(dialog.open).toHaveBeenCalledWith(
        CashOperationDialog,
        expect.objectContaining({ data: { cashAssetId: usdBalanceId, kind } }),
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
            anchorAssetId: plnBalanceId,
            anchorName: 'Safe · PLN',
            currency: 'PLN',
            anchorBalance: 2000,
          },
        }),
      );
    }
  });

  it('links each balance to its transactions', async () => {
    await setup();

    const items = await rowMenuItems(fixture, balanceRow('Safe', 'USD'));

    const link = items.find((item) => /^history$/i.test(menuItemLabel(item)));
    expect(link?.getAttribute('href')).toBe(`/assets/${usdBalanceId}/transactions`);
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
