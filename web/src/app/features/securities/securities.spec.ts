import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatTooltip } from '@angular/material/tooltip';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { SecuritiesResponse } from '../../api/portfolio';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatDate, formatMoney, formatPercent, formatQuantity } from '../../shared/format';
import { clickRowMenuItem, rowMenuItems } from '../../../testing/archive';
import { restoreEnglish, switchLanguage, textOf } from '../../../testing/i18n';
import { ASSET_CLASS } from '../assets/asset-class';
import { AssetFormDialog } from '../assets/asset-form/asset-form-dialog/asset-form-dialog';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
import { TransactionFormDialog } from '../transactions/transaction-form-dialog/transaction-form-dialog';
import { Securities } from './securities';

const PRICE_UNAVAILABLE_REASON = {
  NoQuote: 0,
  FxRateMissing: 1,
  MarketDataUnavailable: 2,
} as const;

const vwce = {
  assetId: '11111111-1111-1111-1111-111111111111',
  portfolioId: '22222222-2222-2222-2222-222222222222',
  portfolioName: 'Alpha',
  name: 'Vanguard FTSE All-World',
  instrumentId: '33333333-3333-3333-3333-333333333333',
  ticker: 'VWCE',
  exchange: 'XETRA',
  currency: 'EUR',
  quantity: 15,
  averageBuyPrice: 110,
  costPln: 6780,
  lastPrice: 120,
  lastPriceDate: '2026-01-30',
  valuePln: 7740,
  unrealizedPl: 150,
  unrealizedPlPercent: 9.09,
  unrealizedPlPln: 960,
};

const cdr = {
  assetId: '44444444-4444-4444-4444-444444444444',
  portfolioId: '55555555-5555-5555-5555-555555555555',
  portfolioName: 'Zeta',
  name: 'CD Projekt',
  instrumentId: '66666666-6666-6666-6666-666666666666',
  ticker: 'CDR',
  exchange: 'GPW',
  currency: 'PLN',
  quantity: 10,
  averageBuyPrice: 200,
  costPln: 2000,
  lastPrice: 150,
  lastPriceDate: '2026-01-30',
  valuePln: 1500,
  unrealizedPl: -500,
  unrealizedPlPercent: -25,
  unrealizedPlPln: -500,
};

const noRateCost = {
  ...vwce,
  assetId: '77777777-7777-7777-7777-777777777777',
  name: 'Xetra ETF bought early',
  ticker: 'XETF',
  costPln: null,
  unrealizedPlPln: null,
};

function unavailable(
  assetId: string,
  name: string,
  reason: number,
): typeof vwce & { priceUnavailableReason: number } {
  return {
    ...vwce,
    assetId,
    name,
    lastPrice: null as never,
    lastPriceDate: null as never,
    valuePln: null as never,
    unrealizedPl: null as never,
    unrealizedPlPercent: null as never,
    unrealizedPlPln: null as never,
    priceUnavailableReason: reason,
  };
}

const stocks: SecuritiesResponse = {
  holdings: [vwce, cdr],
  totals: { valuePln: 9240, costPln: 8780, unrealizedPlPln: 460 },
} as SecuritiesResponse;

const etfs: SecuritiesResponse = {
  holdings: [{ ...vwce, assetId: '88888888-8888-8888-8888-888888888888', name: 'World ETF' }],
  totals: { valuePln: 7740, costPln: 6780, unrealizedPlPln: 960 },
} as SecuritiesResponse;

const empty: SecuritiesResponse = {
  holdings: [],
  totals: { valuePln: 0, costPln: 0, unrealizedPlPln: 0 },
} as SecuritiesResponse;

function isClass(url: string, assetClass: number, name: string): boolean {
  return (
    url.includes('/api/portfolio/securities') &&
    new RegExp(`assetClass=(${assetClass}|${name})\\b`).test(url)
  );
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
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (isClass(url, ASSET_CLASS.Stock, 'Stock')) {
        return jsonResponse(stockBody);
      }
      if (isClass(url, ASSET_CLASS.Etf, 'Etf')) {
        return jsonResponse(etfBody);
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
      'Portfel',
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
    expect(text).toContain('VWCE · XETRA');
    expect(text).toContain('Alpha');
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

  it('colours a gain green and a loss red', async () => {
    await setup();

    const gain = rowFor('Vanguard FTSE All-World').querySelector('[data-testid="gain"]');
    const loss = rowFor('CD Projekt').querySelector('[data-testid="gain"]');

    expect(gain?.classList.contains('positive')).toBe(true);
    expect(loss?.classList.contains('negative')).toBe(true);
  });

  it('shows the quote-currency gain when the PLN gain is unknown', async () => {
    await setup({ holdings: [noRateCost], totals: stocks.totals } as SecuritiesResponse);

    const text = textOf(rowFor('Xetra ETF bought early'));

    expect(text).toContain(money(150, 'EUR'));
    expect(text).toContain(formatPercent(9.09).replace(/\s+/g, ' '));
  });

  it('shows "—" with a tooltip for each reason a price is unavailable', async () => {
    await setup({
      holdings: [
        unavailable('a1', 'No quote holding', PRICE_UNAVAILABLE_REASON.NoQuote),
        unavailable('a2', 'No rate holding', PRICE_UNAVAILABLE_REASON.FxRateMissing),
        unavailable('a3', 'Down holding', PRICE_UNAVAILABLE_REASON.MarketDataUnavailable),
      ],
      totals: empty.totals,
    } as SecuritiesResponse);

    const tooltipIn = (name: string): string | undefined =>
      fixture.debugElement
        .queryAll(By.css('[data-testid="price-unavailable"]'))
        .filter((candidate) => rowFor(name).contains(candidate.nativeElement as HTMLElement))
        .map((candidate) => candidate.injector.get(MatTooltip).message)[0];

    expect(textOf(rowFor('No quote holding'))).toContain('—');
    expect(tooltipIn('No quote holding')).toBe('Brak notowania');
    expect(tooltipIn('No rate holding')).toBe('Brak kursu waluty');
    expect(tooltipIn('Down holding')).toBe('Notowania chwilowo niedostępne');
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
            portfolioId: vwce.portfolioId,
            assetId: vwce.assetId,
            assetClass: ASSET_CLASS.Stock,
            type,
          }),
        }),
      );
      await vi.waitFor(() => expect(listCalls(ASSET_CLASS.Stock, 'Stock')).toBeGreaterThan(before));
    }
  });

  it('links each row to its transactions', async () => {
    await setup();

    const items = await rowMenuItems(fixture, rowFor('Vanguard FTSE All-World'));
    const link = items.find((item) => /transakcje/i.test(textOf(item)));

    expect(link?.getAttribute('href')).toBe(
      `/portfolios/${vwce.portfolioId}/assets/${vwce.assetId}/transactions`,
    );
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
