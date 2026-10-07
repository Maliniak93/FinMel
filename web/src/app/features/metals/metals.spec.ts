import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as marketDataClient } from '../../api/marketdata/client.gen';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { MetalPriceResponse } from '../../api/marketdata';
import type { MetalResponse } from '../../api/portfolio';
import { provideI18nTesting } from '../../core/i18n/testing';
import { formatMoney } from '../../shared/format';
import { clickRowMenuItem } from '../../../testing/archive';
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

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(
    holdings: MetalResponse[] = [goldKrugerrand, silverBar],
    prices: MetalPriceResponse[] = metalPricesGoldOnly,
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (url.includes('/api/marketdata/metal-prices')) {
        return jsonResponse(prices);
      }
      if (url.includes('/api/portfolio/metals')) {
        return jsonResponse(holdings);
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
    const gold = rowFor('Krugerrand').textContent ?? '';
    expect(gold).toContain('Vault');
    expect(gold).toContain('Gold');
    expect(gold).toContain('1 oz');
    const goldValue = Math.round(2 * OUNCE_IN_GRAMS * 522.29 * 100) / 100;
    expect(gold).toContain(formatMoney(goldValue, 'PLN'));

    const silver = rowFor('Silver bar').textContent ?? '';
    expect(silver).toContain('Home');
    expect(silver).toContain('Silver');
    expect(silver).not.toContain('oz');
    expect(silver).toContain('—');
    expect(silver).not.toContain('PLN');
  });

  it('shows a zero value today, not a dash, for a priced holding with no pieces', async () => {
    await setup([metalResponse({ pieces: 0, totalFineGrams: 0 })]);

    const gold = rowFor('Krugerrand').textContent ?? '';
    expect(gold).toContain(formatMoney(0, 'PLN'));
    expect(gold).not.toContain('—');
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
            portfolioId: silverBar.portfolioId,
            assetId: silverBar.assetId,
            assetClass: ASSET_CLASS.PreciousMetal,
            type,
          }),
        }),
      );
      await vi.waitFor(() => expect(holdingsCalls()).toBeGreaterThan(before));
    }
  });
});
