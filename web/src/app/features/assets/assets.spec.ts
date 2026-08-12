import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { client as marketDataClient } from '../../api/marketdata/client.gen';
import type { InstrumentDetailsResponse } from '../../api/marketdata';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import type { AssetResponse, PortfolioResponse } from '../../api/portfolio';
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

// M1.4's third mode: no instrumentId, no manualValue — matches the live shape a Cash asset actually
// comes back as (see M1.11's task brief), which is exactly what M1.7's @else branch got wrong before
// this step (rendered `0,00 €` with a false "Stale — refresh me" chip).
const currencyValuedAsset: AssetResponse = {
  id: '55555555-5555-5555-5555-555555555555',
  portfolioId,
  assetClass: 0, // Cash
  valuationMode: 2, // CurrencyValued
  name: 'Euro Cash',
  currency: 'EUR',
  quantity: 500,
  transactionCount: 1,
};

const plnCashAsset: AssetResponse = {
  id: '66666666-6666-6666-6666-666666666666',
  portfolioId,
  assetClass: 0, // Cash
  valuationMode: 2, // CurrencyValued
  name: 'PLN Cash',
  currency: 'PLN',
  quantity: 500,
  transactionCount: 1,
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
    fxResponse?: Response,
  ): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const url = requestUrl(input);
      if (url.includes('/instruments/')) {
        return instrumentResponse ?? jsonResponse({ detail: 'Not found.' }, 404);
      }
      if (url.includes('/fx/latest-batch')) {
        return fxResponse ?? jsonResponse({ rates: [] });
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

  it('shows a snackbar and does not reload when delete is rejected (asset has transactions)', async () => {
    await setup(jsonResponse([asset]));
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    fetchSpy.mockImplementationOnce(async () =>
      jsonResponse(
        {
          detail: 'Asset has transactions and cannot be removed.',
          errorCode: 'Conflict.AssetHasTransactions',
        },
        409,
      ),
    );

    await component['remove'](asset);

    expect(snackBar.open).toHaveBeenCalledWith(
      'Asset has transactions and cannot be removed.',
      'Dismiss',
    );
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
    expect(text).toContain(new Intl.NumberFormat('pl-PL', { style: 'currency', currency: 'USD' }).format(2120));
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

  it("shows a currency-valued asset's real value, converted through its FX rate, agreeing with quantity × rate — the case that rendered 0,00 before this step", async () => {
    const today = new Date().toISOString().slice(0, 10);
    await setup(
      jsonResponse([currencyValuedAsset]),
      undefined,
      undefined,
      jsonResponse({ rates: [{ pair: 'EURPLN', date: today, rate: 4.3 }] }),
    );
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain(
      new Intl.NumberFormat('pl-PL', { style: 'currency', currency: 'PLN' }).format(2150),
    );
    expect(text).not.toContain('Stale — refresh me');
    expect(text).not.toContain(
      new Intl.NumberFormat('pl-PL', { style: 'currency', currency: 'EUR' }).format(0),
    );
  });

  it('flags a currency-valued asset as stale when its FX rate is older than 7 days, showing the price-staleness marker instead of the manual one', async () => {
    const staleDate = new Date();
    staleDate.setDate(staleDate.getDate() - 10);
    await setup(
      jsonResponse([currencyValuedAsset]),
      undefined,
      undefined,
      jsonResponse({
        rates: [{ pair: 'EURPLN', date: staleDate.toISOString().slice(0, 10), rate: 4.3 }],
      }),
    );
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Stale');
    expect(text).not.toContain('Stale — refresh me');
    expect(text).not.toContain('more than 6 months old');
  });

  it("shows 'No price yet' — never a bogus zero — for a currency-valued asset with no FX rate synced yet", async () => {
    await setup(
      jsonResponse([currencyValuedAsset]),
      undefined,
      undefined,
      jsonResponse({ rates: [] }),
    );
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('No price yet');
    expect(text).not.toContain(
      new Intl.NumberFormat('pl-PL', { style: 'currency', currency: 'EUR' }).format(0),
    );
  });

  it('values a PLN currency-valued asset at its quantity with no FX lookup at all, and never flags it stale', async () => {
    await setup(jsonResponse([plnCashAsset]));
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain(
      new Intl.NumberFormat('pl-PL', { style: 'currency', currency: 'PLN' }).format(500),
    );
    expect(text).not.toContain('Stale');
    expect(
      fetchSpy.mock.calls.some((call: unknown[]) => requestUrl(call[0]).includes('/fx/latest-batch')),
    ).toBe(false);
  });

  it('batches the FX lookup once for multiple currency-valued assets sharing a currency, not per row', async () => {
    const secondEuroCash: AssetResponse = {
      ...currencyValuedAsset,
      id: '77777777-7777-7777-7777-777777777777',
      name: 'Second Euro Cash',
    };
    const today = new Date().toISOString().slice(0, 10);
    await setup(
      jsonResponse([currencyValuedAsset, secondEuroCash]),
      undefined,
      undefined,
      jsonResponse({ rates: [{ pair: 'EURPLN', date: today, rate: 4.3 }] }),
    );
    await fixture.whenStable();

    const fxCalls = fetchSpy.mock.calls.filter((call: unknown[]) =>
      requestUrl(call[0]).includes('/fx/latest-batch'),
    );
    expect(fxCalls.length).toBe(1);
  });
});
