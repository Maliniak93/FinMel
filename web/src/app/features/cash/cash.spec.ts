import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { CashAccountsResponse } from '../../api/portfolio';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatMoney } from '../../shared/format';
import { clickRowMenuItem } from '../../../testing/archive';
import { ASSET_CLASS } from '../assets/asset-class';
import { AssetFormDialog } from '../assets/asset-form/asset-form-dialog/asset-form-dialog';
import { SavingsTransferDialog } from '../deposits/savings-transfer-dialog/savings-transfer-dialog';
import { TransactionFormDialog } from '../transactions/transaction-form-dialog/transaction-form-dialog';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
import { Cash } from './cash';

const plnWallet = {
  assetId: '11111111-1111-1111-1111-111111111111',
  portfolioId: '22222222-2222-2222-2222-222222222222',
  portfolioName: 'Alpha',
  name: 'PLN wallet',
  currency: 'PLN',
  balance: 1200.3,
};

const eurWallet = {
  assetId: '33333333-3333-3333-3333-333333333333',
  portfolioId: '44444444-4444-4444-4444-444444444444',
  portfolioName: 'Zeta',
  name: 'EUR wallet',
  currency: 'EUR',
  balance: 50.25,
};

const populated: CashAccountsResponse = {
  accounts: [plnWallet, eurWallet],
  totals: [
    { currency: 'PLN', balance: 1200.3 },
    { currency: 'EUR', balance: 50.25 },
  ],
};

describe('Cash', () => {
  let fixture: ComponentFixture<Cash>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let failNextLoad: boolean;
  let dialog: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(body: CashAccountsResponse = populated, fail = false): Promise<void> {
    failNextLoad = fail;
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (!requestUrl(input).includes('/api/portfolio/cash-accounts')) {
        return jsonResponse({ detail: 'Not found.' }, 404);
      }
      if (failNextLoad) {
        failNextLoad = false;
        return jsonResponse({ detail: 'Cash is unavailable.' }, 500);
      }
      return jsonResponse(body);
    });

    dialog = { open: vi.fn().mockReturnValue({ afterClosed: () => of(true) }) };

    await TestBed.configureTestingModule({
      imports: [Cash],
      providers: [
        provideI18nTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Cash);
    await fixture.whenStable();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function rows(): HTMLElement[] {
    return Array.from(element().querySelectorAll<HTMLElement>('tbody tr.mat-mdc-row'));
  }

  function rowFor(name: string): HTMLElement {
    const row = rows().find((r) => (r.textContent ?? '').includes(name));
    if (!row) {
      throw new Error(`No row for '${name}'.`);
    }
    return row;
  }

  function cashListCalls(): number {
    return fetchSpy.mock.calls.filter((call: unknown[]) =>
      requestUrl(call[0]).includes('/api/portfolio/cash-accounts'),
    ).length;
  }

  it('lists cash accounts with per-currency totals', async () => {
    await setup();

    expect(rows()).toHaveLength(2);
    const pln = rowFor('PLN wallet').textContent ?? '';
    expect(pln).toContain('Alpha');
    expect(pln).toContain('PLN');
    expect(pln).toContain(formatMoney(1200.3, 'PLN'));
    const eur = rowFor('EUR wallet').textContent ?? '';
    expect(eur).toContain('Zeta');
    expect(eur).toContain('EUR');
    expect(eur).toContain(formatMoney(50.25, 'EUR'));

    const totals = Array.from(element().querySelectorAll<HTMLElement>('.cash-page__total')).map(
      (line) => (line.textContent ?? '').trim(),
    );
    expect(totals).toEqual([
      `PLN: ${formatMoney(1200.3, 'PLN')}`,
      `EUR: ${formatMoney(50.25, 'EUR')}`,
    ]);
    expect(element().querySelector('mat-spinner')).toBeNull();
  });

  it('links each account name to its transactions', async () => {
    await setup();

    const link = rowFor('EUR wallet').querySelector<HTMLAnchorElement>('a');

    expect(link?.textContent).toContain('EUR wallet');
    expect(link?.getAttribute('href')).toBe(
      `/portfolios/${eurWallet.portfolioId}/assets/${eurWallet.assetId}/transactions`,
    );
  });

  it('shows the empty state', async () => {
    await setup({ accounts: [], totals: [] });

    expect(rows()).toHaveLength(0);
    expect(element().querySelector('p')?.textContent).toContain('No cash accounts yet');
    expect(element().querySelector('[role=alert]')).toBeNull();
  });

  it('shows the load error with retry', async () => {
    await setup(populated, true);

    const alert = element().querySelector<HTMLElement>('[role=alert]');
    expect(alert?.textContent).toContain('Cash is unavailable.');
    const retry = Array.from(alert!.querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      (b.textContent ?? '').includes('Retry'),
    );
    expect(retry).toBeDefined();
    expect(cashListCalls()).toBe(1);

    retry!.click();
    await fixture.whenStable();

    expect(cashListCalls()).toBe(2);
    expect(element().querySelector('[role=alert]')).toBeNull();
    expect(rows()).toHaveLength(2);
  });

  function addButton(): HTMLButtonElement | undefined {
    return Array.from(element().querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      /add cash account/i.test(b.textContent ?? ''),
    );
  }

  async function expectAddOpensAssetDialogAndReloads(): Promise<void> {
    const before = cashListCalls();

    addButton()!.click();
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledTimes(1);
    const [component, config] = dialog.open.mock.calls[0];
    expect(component).toBe(AssetFormDialog);
    expect(config.data.assetClass).toBe(ASSET_CLASS.Cash);
    expect(config.data.portfolioId).toBeUndefined();
    await vi.waitFor(() => expect(cashListCalls()).toBeGreaterThan(before));
  }

  it('adds a cash account and reloads the list', async () => {
    await setup();

    expect(addButton()).toBeDefined();
    await expectAddOpensAssetDialogAndReloads();
  });

  it('adds a cash account from the empty state and reloads the list', async () => {
    await setup({ accounts: [], totals: [] });

    await expectAddOpensAssetDialogAndReloads();
  });

  it('opens a deposit or withdrawal for the row and reloads', async () => {
    await setup();

    for (const [label, type] of [
      [/^deposit$/i, 2],
      [/^withdraw$/i, 3],
    ] as const) {
      dialog.open.mockClear();
      const before = cashListCalls();

      await clickRowMenuItem(fixture, rowFor('EUR wallet'), label);

      expect(dialog.open).toHaveBeenCalledWith(
        TransactionFormDialog,
        expect.objectContaining({
          data: expect.objectContaining({
            portfolioId: eurWallet.portfolioId,
            assetId: eurWallet.assetId,
            assetClass: ASSET_CLASS.Cash,
            type,
          }),
        }),
      );
      await vi.waitFor(() => expect(cashListCalls()).toBeGreaterThan(before));
    }
  });

  it('opens the savings transfer for the row and reloads', async () => {
    await setup();
    const before = cashListCalls();

    await clickRowMenuItem(fixture, rowFor('EUR wallet'), /transfer with savings/i);

    expect(dialog.open).toHaveBeenCalledWith(
      SavingsTransferDialog,
      expect.objectContaining({ data: { cash: eurWallet } }),
    );
    await vi.waitFor(() => expect(cashListCalls()).toBeGreaterThan(before));
  });
});
