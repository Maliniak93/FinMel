import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { SecuritiesResponse, WatchlistItemResponse } from '../../api/portfolio';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatDate, formatMoney, formatPercent, formatQuantity } from '../../shared/format';
import { clickRowMenuItem } from '../../../testing/archive';
import { restoreEnglish, switchLanguage, textOf } from '../../../testing/i18n';
import { ASSET_CLASS } from '../assets/asset-class';
import { AssetFormDialog } from '../assets/asset-form/asset-form-dialog/asset-form-dialog';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
import { TransactionFormDialog } from '../transactions/transaction-form-dialog/transaction-form-dialog';
import { Securities } from './securities';
import { WatchlistBuyDialog } from './watchlist-buy-dialog/watchlist-buy-dialog';
import { isWatchlistList, watchlistItem } from './testing/watchlist-fixtures';
import { cdr, empty, etfs, noRateCost, stocks, vwce } from './testing/securities-fixtures';

function isClass(url: string, assetClass: number, name: string): boolean {
  return (
    url.includes('/api/portfolio/securities') &&
    new RegExp(`assetClass=(${assetClass}|${name})\\b`).test(url)
  );
}

function watchlistOf(url: string, items: WatchlistItemResponse[]): WatchlistItemResponse[] {
  const assetClass = new URL(url).searchParams.get('assetClass');
  return items.filter((item) => String(item.assetClass) === assetClass);
}

describe('Securities page', () => {
  let fixture: ComponentFixture<Securities>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(
    stockBody: SecuritiesResponse = stocks,
    etfBody: SecuritiesResponse = etfs,
    watchlistBody: WatchlistItemResponse[] = [],
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (isClass(url, ASSET_CLASS.Stock, 'Stock')) {
        return jsonResponse(stockBody);
      }
      if (isClass(url, ASSET_CLASS.Etf, 'Etf')) {
        return jsonResponse(etfBody);
      }
      if (isWatchlistList(input)) {
        return jsonResponse(watchlistOf(url, watchlistBody));
      }
      return jsonResponse({ detail: 'Not found.' }, 404);
    });
    dialog = { open: vi.fn().mockReturnValue({ afterClosed: () => of(true) }) };

    await TestBed.configureTestingModule({
      imports: [Securities],
      providers: [
        provideI18nTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Securities);
    await fixture.whenStable();
    await switchLanguage(fixture, 'pl');
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function tabs(): HTMLElement[] {
    return Array.from(element().querySelectorAll<HTMLElement>('[role="tab"]'));
  }

  function rows(): HTMLElement[] {
    return Array.from(element().querySelectorAll<HTMLElement>('tbody tr.mat-mdc-row'));
  }

  function rowFor(name: string): HTMLElement {
    const row = rows().find((candidate) => textOf(candidate).includes(name));
    if (!row) {
      throw new Error(`No row for '${name}'.`);
    }
    return row;
  }

  function money(value: number, currency: string): string {
    return formatMoney(value, currency).replace(/\s+/g, ' ');
  }

  function bodyAt(index: number): HTMLElement {
    const body = element().querySelectorAll<HTMLElement>('mat-tab-body')[index];
    if (!body) {
      throw new Error(`No tab body ${index}.`);
    }
    return body;
  }

  function within(root: HTMLElement, selector: string): HTMLElement {
    const found = root.querySelector<HTMLElement>(selector);
    if (!found) {
      throw new Error(`No ${selector} in the tab.`);
    }
    return found;
  }

  async function openEtfTab(): Promise<void> {
    tabs()
      .find((tab) => textOf(tab).includes('ETF'))!
      .click();
    fixture.detectChanges();
    await fixture.whenStable();
    await vi.waitFor(() => {
      fixture.detectChanges();
      const etfBody = element().querySelectorAll('mat-tab-body')[1];
      expect(tabs()[1].getAttribute('aria-selected')).toBe('true');
      expect(etfBody.querySelector('app-securities-table')).not.toBeNull();
    });
  }

  function listCalls(assetClass: number, name: string): number {
    return fetchSpy.mock.calls.filter((call: unknown[]) =>
      isClass(requestUrl(call[0]), assetClass, name),
    ).length;
  }

  function addButton(): HTMLButtonElement | undefined {
    return Array.from(element().querySelectorAll<HTMLButtonElement>('button')).find((button) =>
      textOf(button).includes('Dodaj'),
    );
  }

  it('shows the "Akcje" and "ETF" tabs, opening on "Akcje"', async () => {
    await setup();

    expect(tabs()).toHaveLength(2);
    expect(textOf(tabs()[0])).toContain('Akcje');
    expect(textOf(tabs()[1])).toContain('ETF');
    expect(tabs()[0].getAttribute('aria-selected')).toBe('true');
  });

  it('lists each holding with its columns and the totals below the table', async () => {
    await setup();

    const headers = Array.from(element().querySelectorAll('th')).map(textOf);
    for (const header of [
      'Instrument',
      'Rachunek',
      'Ilość',
      'Śr. cena zakupu',
      'Kurs',
      'Wartość',
      'Zysk/strata',
    ]) {
      expect(headers).toContain(header);
    }
    expect(rows()).toHaveLength(2);
    const text = textOf(rowFor('Vanguard FTSE All-World'));
    expect(text).toContain('Akcje · 15 szt. · ' + money(120, 'EUR'));
    expect(text).toContain('XTB IKE');
    expect(text).toContain(formatQuantity(15));
    expect(text).toContain(money(110, 'EUR'));
    expect(text).toContain(money(120, 'EUR'));
    expect(text).toContain(formatDate('2026-01-30').replace(/\s+/g, ' '));
    expect(text).toContain(money(7740, 'PLN'));
    expect(text).toContain(money(960, 'PLN'));
    expect(text).toContain(formatPercent(9.09).replace(/\s+/g, ' '));
    const footer = textOf(element().querySelector('tr.mat-mdc-footer-row'));
    expect(footer).toContain(money(9240, 'PLN'));
    expect(footer).toContain(money(460, 'PLN'));
  });

  it('shows the quote-currency gain when the PLN gain is unknown', async () => {
    await setup({ holdings: [noRateCost], totals: stocks.totals } as SecuritiesResponse);

    const text = textOf(rowFor('Xetra ETF bought early'));

    expect(text).toContain(money(150, 'EUR'));
    expect(text).toContain(formatPercent(9.09).replace(/\s+/g, ' '));
  });

  it('shows the empty state of each tab with the add button', async () => {
    await setup(empty, empty);

    expect(rows()).toHaveLength(0);
    expect(textOf(element())).toContain('Nie masz jeszcze akcji');
    expect(addButton()).toBeDefined();

    await openEtfTab();

    expect(textOf(element())).toContain('Nie masz jeszcze ETF-ów');
    expect(addButton()).toBeDefined();
  });

  it('opens a buy, sell or dividend for the row and reloads the tab', async () => {
    await setup();

    for (const [label, type] of [
      [/^kup$/i, 0],
      [/^sprzedaj$/i, 1],
      [/^dywidenda$/i, 4],
    ] as const) {
      dialog.open.mockClear();
      const before = listCalls(ASSET_CLASS.Stock, 'Stock');

      await clickRowMenuItem(fixture, rowFor('Vanguard FTSE All-World'), label);

      expect(dialog.open).toHaveBeenCalledWith(
        TransactionFormDialog,
        expect.objectContaining({
          data: expect.objectContaining({
            assetId: vwce.assetId,
            assetClass: ASSET_CLASS.Stock,
            type,
          }),
        }),
      );
      await vi.waitFor(() => expect(listCalls(ASSET_CLASS.Stock, 'Stock')).toBeGreaterThan(before));
    }
  });

  it('buy from the watchlist reloads the holdings table of its tab', async () => {
    await setup(stocks, etfs, [watchlistItem({ assetClass: ASSET_CLASS.Stock })]);
    dialog.open.mockImplementation((component: unknown) => ({
      afterClosed: () =>
        of(
          component === WatchlistBuyDialog
            ? { accountId: vwce.accountId, assetId: vwce.assetId }
            : true,
        ),
    }));
    const holdingsBefore = listCalls(ASSET_CLASS.Stock, 'Stock');
    const watchlistGets = (): number =>
      fetchSpy.mock.calls.filter((call: unknown[]) => isWatchlistList(call[0])).length;
    const watchlistBefore = watchlistGets();
    const watchlistRow = element().querySelector<HTMLElement>('app-watchlist-table tbody tr');
    if (!watchlistRow) {
      throw new Error('No watchlist row rendered.');
    }

    await clickRowMenuItem(fixture, watchlistRow, /^kup$/i);

    await vi.waitFor(() => {
      expect(listCalls(ASSET_CLASS.Stock, 'Stock')).toBeGreaterThan(holdingsBefore);
      expect(watchlistGets()).toBeGreaterThan(watchlistBefore);
    });
  });

  it('subtabs', async () => {
    await setup(stocks, etfs, [
      watchlistItem({
        id: 'b3333333-3333-3333-3333-333333333333',
        assetClass: ASSET_CLASS.Stock,
        name: 'Stock watch',
        ticker: 'STKW',
      }),
      watchlistItem({
        id: 'b4444444-4444-4444-4444-444444444444',
        assetClass: ASSET_CLASS.Etf,
        name: 'ETF watch',
        ticker: 'ETFW',
      }),
    ]);
    const stockTable = within(bodyAt(0), 'app-securities-table');
    const stockWatchlist = within(bodyAt(0), 'app-watchlist-table');
    await vi.waitFor(() => expect(textOf(stockWatchlist)).toContain('Stock watch'));
    expect(textOf(stockTable)).toContain('CD Projekt');
    expect(textOf(stockTable)).not.toContain('World ETF');
    expect(textOf(stockWatchlist)).not.toContain('ETF watch');
    expect(
      stockTable.compareDocumentPosition(stockWatchlist) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();

    await openEtfTab();
    const etfTable = within(bodyAt(1), 'app-securities-table');
    const etfWatchlist = within(bodyAt(1), 'app-watchlist-table');
    await vi.waitFor(() => expect(textOf(etfWatchlist)).toContain('ETF watch'));
    expect(textOf(etfTable)).toContain('World ETF');
    expect(textOf(etfTable)).not.toContain('CD Projekt');
    expect(textOf(etfWatchlist)).not.toContain('Stock watch');
    expect(
      etfTable.compareDocumentPosition(etfWatchlist) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it('shows a Rachunek column with the account name of each holding', async () => {
    await setup();

    const headers = Array.from(element().querySelectorAll('th'), (th) => textOf(th));
    expect(headers).toContain('Rachunek');
    expect(textOf(rowFor('Vanguard FTSE All-World'))).toContain('XTB IKE');
    expect(textOf(rowFor('CD Projekt'))).toContain('XTB zwykly');
  });

  it('filters the holdings by the account query parameter', async () => {
    await setup();

    fixture.componentRef.setInput('account', cdr.accountId);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(rows().map((row) => textOf(row))).toHaveLength(1);
    expect(textOf(rowFor('CD Projekt'))).toContain('XTB zwykly');
  });

  it('shows the average buy price in PLN', async () => {
    await setup({
      holdings: [
        { ...vwce, averageBuyPricePln: 432.5 },
        { ...cdr, averageBuyPricePln: null },
      ],
      totals: stocks.totals,
    } as SecuritiesResponse);

    const headers = Array.from(element().querySelectorAll('th'), (th) => textOf(th));
    const column = headers.indexOf('Śr. cena (PLN)');
    expect(column).toBeGreaterThanOrEqual(0);
    const cell = (name: string): string =>
      textOf(rowFor(name).querySelectorAll<HTMLElement>('td')[column]);
    expect(cell('Vanguard FTSE All-World').replace(/\s+/g, ' ')).toContain(money(432.5, 'PLN'));
    expect(cell('CD Projekt')).toBe('—');
  });

  it('shows every holding when no account is selected', async () => {
    await setup();

    expect(rows()).toHaveLength(2);
  });

  it('adds a holding of the tab class and reloads the tab', async () => {
    await setup();
    const before = listCalls(ASSET_CLASS.Stock, 'Stock');

    addButton()!.click();
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledTimes(1);
    const [component, config] = dialog.open.mock.calls[0];
    expect(component).toBe(AssetFormDialog);
    expect(config.data.assetClass).toBe(ASSET_CLASS.Stock);
    await vi.waitFor(() => expect(listCalls(ASSET_CLASS.Stock, 'Stock')).toBeGreaterThan(before));

    dialog.open.mockClear();
    await openEtfTab();
    addButton()!.click();
    await fixture.whenStable();

    expect(dialog.open.mock.calls[0][1].data.assetClass).toBe(ASSET_CLASS.Etf);
  });
});
