import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { ConfirmDialog } from '../../../shared/confirm-dialog/confirm-dialog';
import { formatDate, formatMoney, formatQuantity } from '../../../shared/format';
import { clickRowMenuItem, menuItemLabel, rowMenuItems } from '../../../../testing/archive';
import { restoreEnglish, switchLanguage, textOf } from '../../../../testing/i18n';
import { ASSET_CLASS } from '../../assets/asset-class';
import { AssetFormDialog } from '../../assets/asset-form/asset-form-dialog/asset-form-dialog';
import { jsonResponse } from '../../assets/asset-form/testing/asset-form-fixtures';
import { TransactionFormDialog } from '../../transactions/transaction-form-dialog/transaction-form-dialog';
import { TRANSACTION_TYPE_BUY } from '../../transactions/transaction-type';
import { WatchlistItemDialog } from '../watchlist-item-dialog/watchlist-item-dialog';
import { WatchlistBuyDialog } from '../watchlist-buy-dialog/watchlist-buy-dialog';
import {
  accountAId,
  accountBId,
  assetInAId,
  isWatchlistList,
  methodOf,
  newAssetId,
  pathOf,
  watchlistItem,
  watchlistItemId,
} from '../testing/watchlist-fixtures';
import { WatchlistTable } from './watchlist-table';

describe('WatchlistTable', () => {
  let fixture: ComponentFixture<WatchlistTable>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let outcomes: Map<unknown, unknown>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(
    items: unknown[] | Response | Promise<Response> = [watchlistItem()],
    settle = true,
  ): Promise<void> {
    outcomes = new Map();
    dialog = {
      open: vi.fn((component: unknown) => ({ afterClosed: () => of(outcomes.get(component)) })),
    };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (isWatchlistList(input)) {
        return Array.isArray(items) ? jsonResponse(items) : items;
      }
      if (methodOf(input) === 'DELETE') {
        return new Response(null, { status: 204 });
      }
      return jsonResponse({ detail: 'Not found.' }, 404);
    });

    await TestBed.configureTestingModule({
      imports: [WatchlistTable],
      providers: [
        provideI18nTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(WatchlistTable);
    fixture.componentRef.setInput('assetClass', ASSET_CLASS.Etf);
    if (!settle) {
      fixture.detectChanges();
      return;
    }
    await fixture.whenStable();
    await switchLanguage(fixture, 'pl');
  }

  function money(amount: number, currency: string): string {
    return formatMoney(amount, currency).replace(/\s+/g, ' ');
  }

  function answer(component: unknown, value: unknown): void {
    outcomes.set(component, value);
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function rows(): HTMLElement[] {
    return Array.from(element().querySelectorAll<HTMLElement>('tbody tr.mat-mdc-row'));
  }

  function rowFor(ticker: string): HTMLElement {
    const row = rows().find((candidate) => textOf(candidate).includes(ticker));
    if (!row) {
      throw new Error(`No row for '${ticker}'.`);
    }
    return row;
  }

  function obserwujButtons(): HTMLButtonElement[] {
    return Array.from(element().querySelectorAll<HTMLButtonElement>('button')).filter((button) =>
      textOf(button).includes('Obserwuj'),
    );
  }

  function watchlistGets(): number {
    return fetchSpy.mock.calls.filter((call: unknown[]) => isWatchlistList(call[0])).length;
  }

  function deletedPaths(): string[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0])
      .filter((input: unknown) => methodOf(input) === 'DELETE')
      .map((input: unknown) => pathOf(input));
  }

  it('renders an item with price fields', async () => {
    await setup([watchlistItem()]);

    const row = rowFor('VWRA');
    const text = textOf(row);

    expect(text).toContain('VWRA');
    expect(text).toContain(formatDate('2026-08-04').replace(/\s+/g, ' '));
    expect(text).toContain(formatQuantity(15));
    expect(text).toContain(money(120, 'USD'));
    expect(text).toContain(money(440.12, 'PLN'));
    const amounts = Array.from(row.querySelectorAll('app-money'), (amount) => textOf(amount));
    expect(amounts).toEqual(expect.arrayContaining([money(120, 'USD'), money(125, 'USD')]));
    expect(row.querySelector('[data-testid="daily-change"]')?.classList.contains('positive')).toBe(
      true,
    );
    expect(row.classList.contains('at-target')).toBe(true);
    expect((await rowMenuItems(fixture, row)).map(menuItemLabel)).toEqual([
      'Kup',
      'Edytuj',
      'Usuń',
    ]);
  });

  it('states', async () => {
    await setup(new Promise<Response>(() => undefined), false);
    expect(element().querySelector('app-skeleton')).not.toBeNull();
    fetchSpy.mockRestore();
    TestBed.resetTestingModule();

    await setup(jsonResponse({ detail: 'Boom.' }, 500));
    const error = element().querySelector('app-section-error');
    expect(textOf(error)).toContain('Ponów');
    fetchSpy.mockRestore();
    TestBed.resetTestingModule();

    await setup([]);
    expect(element().querySelector('app-empty-state')).not.toBeNull();
  });

  it('shows the empty state', async () => {
    await setup([]);

    expect(rows()).toHaveLength(0);
    expect(obserwujButtons().length).toBeGreaterThanOrEqual(2);
  });

  it('adds through the dialog and reloads the list', async () => {
    await setup([]);
    answer(WatchlistItemDialog, true);
    const before = watchlistGets();

    obserwujButtons()[0].click();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledWith(
      WatchlistItemDialog,
      expect.objectContaining({
        data: expect.objectContaining({ assetClass: ASSET_CLASS.Etf }),
      }),
    );
    await vi.waitFor(() => expect(watchlistGets()).toBeGreaterThan(before));
  });

  it('removes after confirm', async () => {
    await setup([watchlistItem()]);
    answer(ConfirmDialog, true);
    const before = watchlistGets();

    await clickRowMenuItem(fixture, rowFor('VWRA'), /^usuń$/i);

    expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
    await vi.waitFor(() => {
      expect(deletedPaths()).toEqual([`/api/portfolio/watchlist/${watchlistItemId}`]);
      expect(watchlistGets()).toBeGreaterThan(before);
    });
  });

  it('cancel sends nothing', async () => {
    await setup([watchlistItem()]);
    answer(ConfirmDialog, false);
    const before = watchlistGets();

    await clickRowMenuItem(fixture, rowFor('VWRA'), /^usuń$/i);
    await fixture.whenStable();

    expect(deletedPaths()).toEqual([]);
    expect(watchlistGets()).toBe(before);
  });

  it('buy into an existing holding opens the transaction dialog', async () => {
    await setup([watchlistItem()]);
    answer(WatchlistBuyDialog, { accountId: accountAId, assetId: assetInAId });
    answer(TransactionFormDialog, true);
    const before = watchlistGets();

    await clickRowMenuItem(fixture, rowFor('VWRA'), /^kup$/i);

    expect(dialog.open).toHaveBeenNthCalledWith(
      1,
      WatchlistBuyDialog,
      expect.objectContaining({
        data: expect.objectContaining({ item: expect.objectContaining({ id: watchlistItemId }) }),
      }),
    );
    expect(dialog.open).toHaveBeenNthCalledWith(
      2,
      TransactionFormDialog,
      expect.objectContaining({
        data: expect.objectContaining({
          assetId: assetInAId,
          accountId: accountAId,
          assetClass: ASSET_CLASS.Etf,
          currency: 'USD',
          type: TRANSACTION_TYPE_BUY,
        }),
      }),
    );
    expect(dialog.open).toHaveBeenCalledTimes(2);
    await vi.waitFor(() => expect(watchlistGets()).toBeGreaterThan(before));
  });

  it('buy into a new position chains asset and transaction dialogs', async () => {
    await setup([watchlistItem()]);
    answer(WatchlistBuyDialog, { accountId: accountBId, assetId: null });
    answer(AssetFormDialog, newAssetId);
    answer(TransactionFormDialog, true);

    await clickRowMenuItem(fixture, rowFor('VWRA'), /^kup$/i);

    expect(dialog.open).toHaveBeenNthCalledWith(
      2,
      AssetFormDialog,
      expect.objectContaining({
        data: expect.objectContaining({
          accountId: accountBId,
          assetClass: ASSET_CLASS.Etf,
          instrumentId: watchlistItem().instrumentId,
        }),
      }),
    );
    expect(dialog.open).toHaveBeenNthCalledWith(
      3,
      TransactionFormDialog,
      expect.objectContaining({
        data: expect.objectContaining({
          assetId: newAssetId,
          accountId: accountBId,
          assetClass: ASSET_CLASS.Etf,
          type: TRANSACTION_TYPE_BUY,
        }),
      }),
    );
    expect(dialog.open).toHaveBeenCalledTimes(3);
  });

  it('cancelled asset dialog stops the chain', async () => {
    await setup([watchlistItem()]);
    answer(WatchlistBuyDialog, { accountId: accountBId, assetId: null });
    answer(AssetFormDialog, false);

    await clickRowMenuItem(fixture, rowFor('VWRA'), /^kup$/i);
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledTimes(2);
    expect(dialog.open.mock.calls.map((call: unknown[]) => call[0])).not.toContain(
      TransactionFormDialog,
    );
  });
});
