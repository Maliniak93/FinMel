import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as marketDataClient } from '../../api/marketdata/client.gen';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { MetalPriceResponse } from '../../api/marketdata';
import type { MetalResponse } from '../../api/portfolio';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatMoney, formatQuantity, formatSignedMoney } from '../../shared/format';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import {
  clickRowMenuItem,
  menuItemLabel,
  rowMenuItems,
  showArchived,
} from '../../../testing/archive';
import { restoreEnglish, switchLanguage, textOf } from '../../../testing/i18n';
import {
  goldKrugerrand,
  metalPricesGoldOnly,
  metalResponse,
  OUNCE_IN_GRAMS,
  silverBar,
} from '../../../testing/metal-fixtures';
import { ASSET_CLASS } from '../assets/asset-class';
import { jsonResponse, requestUrl } from '../assets/asset-form/testing/asset-form-fixtures';
import { TransactionFormDialog } from '../transactions/transaction-form-dialog/transaction-form-dialog';
import { MetalFormDialog } from './metal-form-dialog/metal-form-dialog';
import { Metals } from './metals';

describe('Metals', () => {
  let fixture: ComponentFixture<Metals>;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(
    holdings: MetalResponse[] | Response | Promise<Response> = [goldKrugerrand, silverBar],
    prices: MetalPriceResponse[] = metalPricesGoldOnly,
    settle = true,
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      const url = requestUrl(input);
      if (request.method === 'DELETE' && Array.isArray(holdings)) {
        const index = holdings.findIndex((holding) => url.endsWith(`/${holding.assetId}`));
        if (index >= 0) {
          holdings.splice(index, 1);
        }
        return new Response(null, { status: 204 });
      }
      if (request.method === 'POST' && /\/assets\/[^/]+\/(archive|restore)$/.test(url)) {
        return jsonResponse({});
      }
      if (url.includes('/api/marketdata/metal-prices')) {
        return jsonResponse(prices);
      }
      if (url.includes('/api/portfolio/metals')) {
        return Array.isArray(holdings) ? jsonResponse(holdings) : holdings;
      }
      return jsonResponse({ detail: 'Not found.' }, 404);
    });
    dialog = { open: vi.fn().mockReturnValue({ afterClosed: () => of(true) }) };

    await TestBed.configureTestingModule({
      imports: [Metals],
      providers: [
        provideI18nTesting(),
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Metals);
    if (!settle) {
      fixture.detectChanges();
      return;
    }
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

  function holdingsCalls(): number {
    return fetchSpy.mock.calls.filter((call: unknown[]) =>
      requestUrl(call[0]).includes('/api/portfolio/metals'),
    ).length;
  }

  function writes(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((request: Request) => request.method !== 'GET');
  }

  function addButton(): HTMLButtonElement | undefined {
    return Array.from(element().querySelectorAll<HTMLButtonElement>('button')).find((b) =>
      /add precious metal/i.test(b.textContent ?? ''),
    );
  }

  it('shows the price header with both metals, silver without a price as a dash', async () => {
    await setup();

    const header = element().querySelector<HTMLElement>('.metals-page__prices');
    const text = header?.textContent ?? '';
    expect(text).toContain('Gold');
    expect(text).toContain('Silver');
    expect(text).toContain(formatMoney(522.29, 'PLN'));
    expect(text).toContain(formatMoney(16245.5, 'PLN'));
    expect(text).toContain('—');
  });

  it('lists holdings with weight, pieces, total fine weight and value today', async () => {
    await setup();

    expect(rows()).toHaveLength(2);
    const gold = (rowFor('Krugerrand').textContent ?? '').replace(/\s+/g, ' ');
    expect(gold).toContain('Gold');
    expect(gold).toContain('31.1 g (1 oz)');
    expect(gold).toContain('62.21 g');
    const goldValue = Math.round(2 * OUNCE_IN_GRAMS * 522.29 * 100) / 100;
    expect(gold).toContain(formatMoney(goldValue, 'PLN').replace(/\s+/g, ' '));

    const silver = rowFor('Silver bar').textContent ?? '';
    expect(silver).toContain('Silver');
    expect(silver).not.toContain('oz');
    expect(silver).toContain('—');
    expect(silver).not.toContain('PLN');
  });

  it('links each holding to its transactions', async () => {
    await setup();

    const link = rowFor(goldKrugerrand.name).querySelector<HTMLAnchorElement>(
      'a[href*="/transactions"]',
    );
    expect(link?.getAttribute('href')).toBe(
      `/wealth/assets/${goldKrugerrand.assetId}/transactions`,
    );
  });

  it('shows a zero value today, not a dash, for a priced holding with no pieces', async () => {
    await setup([metalResponse({ pieces: 0, totalFineGrams: 0 })]);

    const value = rowFor('Krugerrand').querySelector('app-money')?.textContent ?? '';
    expect(value).toContain(formatMoney(0, 'PLN'));
    expect(value).not.toContain('—');
  });

  it('shows the empty state with an add button when there are no holdings', async () => {
    await setup([]);

    expect(rows()).toHaveLength(0);
    expect(element().textContent).toContain('No precious metals yet');
    expect(addButton()).toBeDefined();
  });

  it('opens the metal dialog from the add button and reloads', async () => {
    await setup();
    const before = holdingsCalls();

    addButton()!.click();
    await fixture.whenStable();

    expect(dialog.open).toHaveBeenCalledTimes(1);
    expect(dialog.open.mock.calls[0][0]).toBe(MetalFormDialog);
    await vi.waitFor(() => expect(holdingsCalls()).toBeGreaterThan(before));
  });

  it('opens the metal dialog for the row on Edit', async () => {
    await setup();

    await clickRowMenuItem(fixture, rowFor('Krugerrand'), /^edit$/i);

    expect(dialog.open).toHaveBeenCalledWith(
      MetalFormDialog,
      expect.objectContaining({
        data: expect.objectContaining({
          metal: expect.objectContaining({ assetId: goldKrugerrand.assetId }),
        }),
      }),
    );
  });

  it('opens the transaction dialog preset to Buy or Sell for the row and reloads', async () => {
    await setup();

    for (const [label, type] of [
      [/^buy$/i, 0],
      [/^sell$/i, 1],
    ] as const) {
      dialog.open.mockClear();
      const before = holdingsCalls();

      await clickRowMenuItem(fixture, rowFor('Silver bar'), label);

      expect(dialog.open).toHaveBeenCalledWith(
        TransactionFormDialog,
        expect.objectContaining({
          data: expect.objectContaining({
            assetId: silverBar.assetId,
            assetClass: ASSET_CLASS.PreciousMetal,
            type,
          }),
        }),
      );
      await vi.waitFor(() => expect(holdingsCalls()).toBeGreaterThan(before));
    }
  });

  it('stale price and archived holding', async () => {
    const archivedCoin = metalResponse({
      assetId: '44444444-eeee-4444-eeee-444444444444',
      name: 'Archived coin',
      isArchived: true,
    });
    await setup(
      [goldKrugerrand, archivedCoin],
      [{ ...metalPricesGoldOnly[0], isStale: true }, metalPricesGoldOnly[1]],
    );
    await showArchived(fixture);

    const goldStrip = Array.from(
      element().querySelectorAll<HTMLElement>('.metals-page__price'),
    ).find((strip) => textOf(strip).includes('Gold'));
    expect(textOf(goldStrip?.querySelector('app-status-chip') ?? null)).toContain('Stale');

    const archivedRow = rowFor('Archived coin');
    expect(textOf(archivedRow.querySelector('app-status-chip'))).toContain('Archived');
    const items = await rowMenuItems(fixture, archivedRow);
    for (const label of [/^edit$/i, /^buy$/i, /^sell$/i]) {
      expect(isDisabled(menuItem(items, label))).toBe(true);
    }
    expect(isDisabled(menuItem(items, /transaction history/i))).toBe(false);
  });

  it('hides archived holdings until the toggle is on', async () => {
    const archivedCoin = metalResponse({
      assetId: '44444444-eeee-4444-eeee-444444444444',
      name: 'Archived coin',
      isArchived: true,
    });
    await setup([goldKrugerrand, archivedCoin]);

    expect(rows()).toHaveLength(1);
    expect(element().textContent).not.toContain('Archived coin');

    await showArchived(fixture);

    expect(rows()).toHaveLength(2);
    expect(textOf(rowFor('Archived coin').querySelector('app-status-chip'))).toContain('Archived');
    expect(textOf(rowFor('Krugerrand'))).not.toContain('Archived');
  });

  it('archives a holding after confirming', async () => {
    await setup();
    const before = holdingsCalls();

    await clickRowMenuItem(fixture, rowFor('Krugerrand'), /^archive$/i);

    expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
    await vi.waitFor(() => expect(writes()).toHaveLength(1));
    const [request] = writes();
    expect(request.method).toBe('POST');
    expect(request.url).toContain(`/api/portfolio/assets/${goldKrugerrand.assetId}/archive`);
    await vi.waitFor(() => expect(holdingsCalls()).toBeGreaterThan(before));
  });

  it('restores an archived holding', async () => {
    const archivedCoin = metalResponse({
      assetId: '44444444-eeee-4444-eeee-444444444444',
      name: 'Archived coin',
      isArchived: true,
    });
    await setup([goldKrugerrand, archivedCoin]);
    await showArchived(fixture);

    const items = await rowMenuItems(fixture, rowFor('Archived coin'));
    expect(items.map(menuItemLabel)).toEqual(expect.arrayContaining(['Restore', 'Delete']));
    expect(items.map(menuItemLabel)).not.toContain('Archive');
    document.querySelector<HTMLElement>('.cdk-overlay-backdrop')?.click();
    fixture.detectChanges();
    await fixture.whenStable();

    await clickRowMenuItem(fixture, rowFor('Archived coin'), /^restore$/i);

    await vi.waitFor(() => expect(writes()).toHaveLength(1));
    const [request] = writes();
    expect(request.method).toBe('POST');
    expect(request.url).toContain(`/api/portfolio/assets/${archivedCoin.assetId}/restore`);
  });

  it('delete dialog names the transaction count and the held quantity', async () => {
    const coin = metalResponse({
      assetId: '55555555-eeee-4444-eeee-555555555555',
      name: 'Testowy',
      pieces: 1,
      totalFineGrams: 12,
      transactionCount: 3,
    });
    const holdings = [coin, silverBar];
    await setup(holdings);

    await clickRowMenuItem(fixture, rowFor('Testowy'), /^delete$/i);

    expect(dialog.open).toHaveBeenCalledWith(ConfirmDialog, expect.anything());
    const data = dialog.open.mock.calls[0][1].data;
    expect(data.destructive).toBe(true);
    expect(data.message).toContain('3');
    expect(data.message).toContain(`${formatQuantity(12)} g`);
    await vi.waitFor(() =>
      expect(
        writes().some(
          (request) =>
            request.method === 'DELETE' && request.url.endsWith(`/assets/${coin.assetId}`),
        ),
      ).toBe(true),
    );
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(rows().some((row) => (row.textContent ?? '').includes('Testowy'))).toBe(false);
    });
  });

  it('delete dialog shows no transaction count for a metal without transactions', async () => {
    const coin = metalResponse({
      assetId: '66666666-eeee-4444-eeee-666666666666',
      name: 'Pusty',
      pieces: 1,
      totalFineGrams: 12,
      transactionCount: 0,
    });
    await setup([coin]);

    await clickRowMenuItem(fixture, rowFor('Pusty'), /^delete$/i);

    const data = dialog.open.mock.calls[0][1].data;
    expect(data.message).not.toMatch(/\b0\b/);
    expect(data.message).toContain(`${formatQuantity(12)} g`);
  });

  it('gain column', async () => {
    await setup([gainGold, { ...silverBar, costPln: 1600 }, noCostCoin], gainPrices);
    await switchLanguage(fixture, 'pl');

    expect(textOf(element().querySelector('thead'))).toContain('Zysk/strata');
    expect(gainOf(rowFor('Krugerrand'))).toBe('+300,00 zł (+15,00 %)');
    expect(rowFor('Krugerrand').querySelector('.gain--positive')).not.toBeNull();
    const silver = rowFor('Silver bar');
    expect(gainOf(silver)).toContain(formatSignedMoney(-100, 'PLN').replace(/\s+/g, ' '));
    expect(gainOf(silver)).toMatch(/6,25 %/);
    expect(silver.querySelector('.gain--negative')).not.toBeNull();
    expect(gainOf(rowFor('Coin without cost'))).toBe('—');
    expect(rowFor('Coin without cost').querySelector('.gain--empty')).not.toBeNull();
  });

  it('total row', async () => {
    await setup([gainGold, { ...silverBar, costPln: 2500 }, noCostCoin], gainPrices);
    await switchLanguage(fixture, 'pl');

    const footer = element().querySelector('tfoot');
    const text = textOf(footer);
    expect(text).toContain('Razem');
    expect(text).toContain(formatMoney(3915, 'PLN').replace(/\s+/g, ' '));
    expect(text).toContain(formatSignedMoney(-700, 'PLN').replace(/\s+/g, ' '));
    expect(text).toMatch(/15,56 %/);
    expect(text).toContain('bez 1 pozycji');
    expect(footer?.querySelector('.gain--negative')).not.toBeNull();
  });

  it('states', async () => {
    await setup(new Promise<Response>(() => undefined), metalPricesGoldOnly, false);
    expect(element().querySelector('app-skeleton')).not.toBeNull();
    fetchSpy.mockRestore();
    TestBed.resetTestingModule();

    await setup(jsonResponse({ detail: 'Boom.' }, 500));
    expect(textOf(element().querySelector('app-section-error'))).toContain('Retry');
    fetchSpy.mockRestore();
    TestBed.resetTestingModule();

    await setup([]);
    const emptyState = element().querySelector('app-empty-state');
    expect(textOf(emptyState)).toContain('No precious metals yet');
    expect(textOf(emptyState)).toContain('Add precious metal');
  });
});

const gainGold = metalResponse({
  name: 'Krugerrand',
  pieces: 2,
  fineWeightGramsPerPiece: 10,
  totalFineGrams: 20,
  costPln: 2000,
});

const noCostCoin = metalResponse({
  assetId: '55555555-eeee-5555-eeee-555555555555',
  name: 'Coin without cost',
  pieces: 1,
  fineWeightGramsPerPiece: 1,
  totalFineGrams: 1,
  costPln: null,
});

const gainPrices: MetalPriceResponse[] = [
  { ...metalPricesGoldOnly[0], pricePerGramPln: 115, isStale: false },
  { ...metalPricesGoldOnly[1], pricePerGramPln: 3, isStale: false },
];

function gainOf(row: HTMLElement): string {
  return textOf(row.querySelector('app-gain'))
    .replace(/\s+/g, ' ')
    .replace(/(\S)\(/, '$1 (');
}

function menuItem(items: HTMLElement[], label: RegExp): HTMLElement {
  const item = items.find((candidate) => label.test(menuItemLabel(candidate)));
  if (!item) {
    throw new Error(`No menu item matching ${label}.`);
  }
  return item;
}

function isDisabled(item: HTMLElement): boolean {
  return item.hasAttribute('disabled') || item.getAttribute('aria-disabled') === 'true';
}
