import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuTrigger } from '@angular/material/menu';
import { MatSnackBar } from '@angular/material/snack-bar';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as marketDataClient } from '../../api/marketdata/client.gen';
import type { InstrumentDetailsResponse } from '../../api/marketdata';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { AssetResponse, PortfolioResponse } from '../../api/portfolio';
import { formatMoney } from '../../shared/format-money';
import { AssetFormDialog } from './asset-form/asset-form-dialog/asset-form-dialog';
import { VALUATION_MODE } from './asset-valuation-mode';
import { Assets } from './assets';

// See auth.spec.ts: relative-import `vi.mock` is blocked, so this stubs `fetch` (what the
// generated client ultimately calls) instead of mocking the SDK module.
function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const portfolioId = '22222222-2222-2222-2222-222222222222';
const instrumentId = '33333333-3333-3333-3333-333333333333';

const portfolio: PortfolioResponse = {
  id: portfolioId,
  name: 'Retirement',
  description: null,
  currency: 'PLN',
  isArchived: false,
  assetCount: 1,
};

const asset: AssetResponse = {
  id: '11111111-1111-1111-1111-111111111111',
  portfolioId,
  assetClass: 2,
  valuationMode: 1, // Manual
  name: 'Apple',
  currency: 'USD',
  quantity: 10,
  manualValue: 1000,
  manualValueDate: '2020-01-01',
  transactionCount: 0,
};

const marketAsset: AssetResponse = {
  id: '44444444-4444-4444-4444-444444444444',
  portfolioId,
  assetClass: 2,
  valuationMode: 0, // Market
  name: 'Apple',
  currency: 'USD',
  quantity: 10,
  instrumentId,
  transactionCount: 0,
};

const currencyValuedAsset: AssetResponse = {
  id: '55555555-5555-5555-5555-555555555555',
  portfolioId,
  assetClass: 0,
  valuationMode: VALUATION_MODE.CurrencyValued,
  name: 'Cash',
  currency: 'PLN',
  quantity: 1000,
  manualValue: null,
  manualValueDate: null,
  transactionCount: 1,
};

const currencyValuedEurAsset: AssetResponse = {
  ...currencyValuedAsset,
  id: '66666666-6666-6666-6666-666666666666',
  currency: 'EUR',
  quantity: 250,
};

function requestUrl(input: unknown): string {
  return typeof input === 'string' ? input : (input as Request).url;
}

describe('Assets', () => {
  let fixture: ComponentFixture<Assets>;
  let component: Assets;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let snackBar: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(
    assetsResponse: Response,
    portfolioResponse = jsonResponse(portfolio),
    instrumentResponse?: Response,
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (url.includes('/instruments/')) {
        return instrumentResponse ?? jsonResponse({ detail: 'Not found.' }, 404);
      }
      return url.includes('/assets') ? assetsResponse : portfolioResponse;
    });
    dialog = { open: vi.fn() };
    snackBar = { open: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [Assets],
      providers: [
        provideRouter([]),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(Assets);
    fixture.componentRef.setInput('portfolioId', portfolioId);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  it('should create', async () => {
    await setup(jsonResponse([asset]));
    expect(component).toBeTruthy();
  });

  it('loads the portfolio and its assets into their resources', async () => {
    await setup(jsonResponse([asset]));

    expect(component['portfolioResource'].value()).toEqual(portfolio);
    expect(component['assetsResource'].value()).toEqual([asset]);
  });

  it('reports an empty resource when the portfolio has no assets', async () => {
    await setup(jsonResponse([]));

    expect(component['assetsResource'].hasValue()).toBe(true);
    expect(component['assetsResource'].value()).toEqual([]);
  });

  it('surfaces an assets load failure through the resource error', async () => {
    await setup(jsonResponse({ detail: 'Service unavailable.' }, 503));

    expect(component['assetsResource'].error()?.message).toBe('Service unavailable.');
  });

  it('surfaces a portfolio load failure through the resource error', async () => {
    await setup(jsonResponse([asset]), jsonResponse({ detail: 'Not found.' }, 404));

    expect(component['portfolioResource'].error()?.message).toBe('Not found.');
  });

  it('reloads after a successful create-dialog save', async () => {
    await setup(jsonResponse([asset]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;

    component['openCreateDialog']();
    await fixture.whenStable();

    expect(fetchSpy.mock.calls.length).toBeGreaterThan(callsBefore);
  });

  // Spec #105: the create/edit shell lives in asset-form/ now and is 560px wide so the type-picker
  // tile grid fits.
  it('opens the asset-form shell 560px wide for create and edit', async () => {
    await setup(jsonResponse([asset]));
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });

    component['openCreateDialog']();
    component['openEditDialog'](asset);

    expect(dialog.open).toHaveBeenNthCalledWith(
      1,
      AssetFormDialog,
      expect.objectContaining({ width: '560px', data: { portfolioId } }),
    );
    expect(dialog.open).toHaveBeenNthCalledWith(
      2,
      AssetFormDialog,
      expect.objectContaining({ width: '560px', data: { portfolioId, asset } }),
    );
  });

  it('does not reload when the create dialog is dismissed without saving', async () => {
    await setup(jsonResponse([asset]));
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });
    const callsBefore = fetchSpy.mock.calls.length;

    component['openCreateDialog']();
    await fixture.whenStable();

    expect(fetchSpy.mock.calls.length).toBe(callsBefore);
  });

  it('deletes a transaction-less asset after confirmation and reloads', async () => {
    await setup(jsonResponse([asset]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;
    fetchSpy.mockImplementationOnce(async () => new Response(null, { status: 204 }));

    await component['remove'](asset);
    await fixture.whenStable();

    const deleteCall = fetchSpy.mock.calls[callsBefore][0] as Request;
    expect(deleteCall.method).toBe('DELETE');
    expect(deleteCall.url).toContain(asset.id);
    expect(fetchSpy.mock.calls.length).toBeGreaterThan(callsBefore + 1);
  });

  it('does not delete when the confirmation is cancelled', async () => {
    await setup(jsonResponse([asset]));
    dialog.open.mockReturnValue({ afterClosed: () => of(false) });
    const callsBefore = fetchSpy.mock.calls.length;

    await component['remove'](asset);

    expect(fetchSpy.mock.calls.length).toBe(callsBefore);
  });

  // spec-08 AC-10: delete no longer has a 409-specific path, but any server failure still surfaces
  // the ProblemDetails detail in a snackbar and skips the reload.
  it('shows a snackbar and does not reload when delete fails on the server', async () => {
    await setup(jsonResponse([asset]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;
    fetchSpy.mockImplementationOnce(async () =>
      jsonResponse(
        {
          detail: 'Something went wrong while deleting the asset.',
          errorCode: 'Unexpected',
        },
        500,
      ),
    );

    await component['remove'](asset);

    expect(snackBar.open).toHaveBeenCalledWith(
      'Something went wrong while deleting the asset.',
      'Dismiss',
    );
    expect(fetchSpy.mock.calls.length).toBe(callsBefore + 1);
  });

  // spec-08 AC-10: delete cascades to the asset's transactions, so the menu item is always enabled
  // and the confirmation names how many transactions go with it.
  it('deletes an asset with transactions after a confirmation naming the transaction count', async () => {
    const withTransactions: AssetResponse = { ...asset, transactionCount: 3 };
    await setup(jsonResponse([withTransactions]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;
    fetchSpy.mockImplementationOnce(async () => new Response(null, { status: 204 }));

    const overlayContainer = TestBed.inject(OverlayContainer);
    const trigger = fixture.debugElement
      .query(By.directive(MatMenuTrigger))
      .injector.get(MatMenuTrigger);

    trigger.openMenu();
    fixture.detectChanges();
    await fixture.whenStable();

    const deleteButton = Array.from(
      overlayContainer.getContainerElement().querySelectorAll('button'),
    ).find((button) => (button.textContent ?? '').includes('Delete')) as
      HTMLButtonElement | undefined;
    expect(deleteButton?.disabled).toBe(false);
    // No "can't be deleted" tooltip any more — MatTooltip marks its host with this class.
    expect(deleteButton?.classList.contains('mat-mdc-tooltip-trigger')).toBe(false);

    deleteButton!.click();
    await vi.waitFor(() => expect(dialog.open).toHaveBeenCalled());

    const message = (dialog.open.mock.calls[0][1] as { data: { message: string } }).data.message;
    expect(message).toMatch(
      /^"Apple" and its 3 transactions?(\(s\))? will be permanently deleted\. This can't be undone\.$/,
    );

    // The DELETE, then the assets reload.
    await vi.waitFor(() => expect(fetchSpy.mock.calls.length).toBeGreaterThan(callsBefore + 1));
    const deleteCall = fetchSpy.mock.calls[callsBefore][0] as Request;
    expect(deleteCall.method).toBe('DELETE');
    expect(deleteCall.url).toContain(asset.id);
  });

  it("shows a market asset's last price and date", async () => {
    const instrument: InstrumentDetailsResponse = {
      id: instrumentId,
      ticker: 'AAPL.US',
      name: 'Apple Inc.',
      assetClass: 2,
      quoteCurrency: 'USD',
      source: 1,
      verificationStatus: 0,
      lastPrice: 212,
      lastPriceDate: new Date().toISOString().slice(0, 10),
    };
    await setup(jsonResponse([marketAsset]), undefined, jsonResponse(instrument));
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain(
      new Intl.NumberFormat('pl-PL', { style: 'currency', currency: 'USD' }).format(2120),
    );
    expect(text).not.toContain('Stale');
  });

  it('flags a market asset as stale when its last price is older than 7 days', async () => {
    const staleDate = new Date();
    staleDate.setDate(staleDate.getDate() - 10);
    const instrument: InstrumentDetailsResponse = {
      id: instrumentId,
      ticker: 'AAPL.US',
      name: 'Apple Inc.',
      assetClass: 2,
      quoteCurrency: 'USD',
      source: 1,
      verificationStatus: 0,
      lastPrice: 212,
      lastPriceDate: staleDate.toISOString().slice(0, 10),
    };
    await setup(jsonResponse([marketAsset]), undefined, jsonResponse(instrument));
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Stale');
  });

  it("shows 'No price yet' for a market asset with no quotes", async () => {
    const instrument: InstrumentDetailsResponse = {
      id: instrumentId,
      ticker: 'NEW.US',
      name: 'Brand New Co.',
      assetClass: 2,
      quoteCurrency: 'USD',
      source: 1,
      verificationStatus: 1,
      lastPrice: null,
      lastPriceDate: null,
    };
    await setup(jsonResponse([marketAsset]), undefined, jsonResponse(instrument));
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No price yet');
  });

  // spec fix-currency-valued-asset-value AC-1: a currency-valued asset's Value cell is its quantity
  // in its own currency, not its (always-null) manualValue.
  it("shows a currency-valued asset's quantity as its value", async () => {
    await setup(jsonResponse([currencyValuedAsset]));

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain(formatMoney(1000, 'PLN'));
    expect(text).not.toContain('0,00 zł');
  });

  // spec fix-currency-valued-asset-value AC-2: no valuation date exists for a currency-valued asset,
  // so "Valued on" shows an em dash and never the manual Stale chip (its manualValueDate is null,
  // which `isStale` would otherwise treat as 1970-01-01).
  it('does not flag a currency-valued asset as stale', async () => {
    await setup(jsonResponse([currencyValuedAsset]));

    const cell = (fixture.nativeElement as HTMLElement).querySelector(
      'td.mat-column-manualValueDate',
    );
    expect(cell?.textContent).toContain('—');
    expect(cell?.textContent).not.toContain('Stale');
  });

  // spec fix-currency-valued-asset-value AC-3: out of scope explicitly excludes converting to PLN —
  // the list shows the asset's own currency, like a market asset shown in its quote currency.
  it('shows a non-PLN currency-valued asset in its own currency', async () => {
    await setup(jsonResponse([currencyValuedEurAsset]));

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain(formatMoney(250, 'EUR'));
  });

  // spec fix-currency-valued-asset-value AC-4: the manual Stale chip is unchanged for Manual assets —
  // it fires only when the hand-typed valuation date is more than 6 months old.
  it('flags only a manual asset with an old valuation date as stale', async () => {
    const staleDate = new Date();
    staleDate.setMonth(staleDate.getMonth() - 7);
    const freshDate = new Date();
    freshDate.setDate(freshDate.getDate() - 7);

    const staleManualAsset: AssetResponse = {
      ...asset,
      id: '77777777-7777-7777-7777-777777777777',
      manualValueDate: staleDate.toISOString().slice(0, 10),
    };
    const freshManualAsset: AssetResponse = {
      ...asset,
      id: '88888888-8888-8888-8888-888888888888',
      name: 'Fresh',
      manualValueDate: freshDate.toISOString().slice(0, 10),
    };

    await setup(jsonResponse([staleManualAsset, freshManualAsset]));

    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr.mat-mdc-row');
    expect(rows.length).toBe(2);
    expect(rows[0].textContent).toContain('Stale — refresh me');
    expect(rows[1].textContent).not.toContain('Stale');
  });

  // archived-portfolio-out-of-net-worth AC11: the backend rejects every asset write into an archived
  // portfolio with 409, so the page must not offer one — no New asset / Add your first asset, no
  // Edit / Delete in any row menu — and says why instead.
  describe('archived portfolio is read-only', () => {
    const archivedPortfolio: PortfolioResponse = { ...portfolio, isArchived: true };
    const archivedNotice = /This portfolio is archived\W+restore it to make changes/;

    function pageText(): string {
      return (fixture.nativeElement as HTMLElement).textContent ?? '';
    }

    function pageButtonTexts(): string[] {
      return Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
        (button) => button.textContent?.trim() ?? '',
      );
    }

    // Opens every row menu the page renders (if any) and returns what the overlay offers.
    async function rowMenuText(): Promise<string> {
      const overlayContainer = TestBed.inject(OverlayContainer);
      for (const triggerElement of fixture.debugElement.queryAll(By.directive(MatMenuTrigger))) {
        triggerElement.injector.get(MatMenuTrigger).openMenu();
        fixture.detectChanges();
        await fixture.whenStable();
      }
      return overlayContainer.getContainerElement().textContent ?? '';
    }

    it('archived portfolio is read-only: no add / edit / remove actions, archived notice shown', async () => {
      await setup(jsonResponse([asset, currencyValuedAsset]), jsonResponse(archivedPortfolio));

      // The assets themselves are still listed — archived is read-only, not hidden.
      expect(pageText()).toContain('Apple');
      expect(pageText()).toMatch(archivedNotice);
      expect(pageButtonTexts().some((text) => text.includes('New asset'))).toBe(false);

      const menuText = await rowMenuText();
      expect(menuText).not.toContain('Edit');
      expect(menuText).not.toContain('Delete');
    });

    it('archived portfolio is read-only: the empty state offers no add button', async () => {
      await setup(jsonResponse([]), jsonResponse(archivedPortfolio));

      expect(pageText()).toMatch(archivedNotice);
      const buttons = pageButtonTexts();
      expect(buttons.some((text) => text.includes('New asset'))).toBe(false);
      expect(buttons.some((text) => text.includes('Add your first asset'))).toBe(false);
    });

    it('archived portfolio is read-only: an active portfolio keeps its actions and shows no notice', async () => {
      await setup(jsonResponse([asset]));

      expect(pageText()).not.toMatch(archivedNotice);
      expect(pageButtonTexts().some((text) => text.includes('New asset'))).toBe(true);

      const menuText = await rowMenuText();
      expect(menuText).toContain('Edit');
      expect(menuText).toContain('Delete');
    });
  });
});
