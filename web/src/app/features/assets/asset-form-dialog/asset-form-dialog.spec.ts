import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { MatAutocompleteSelectedEvent } from '@angular/material/autocomplete';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNativeDateAdapter } from '@angular/material/core';

import { client as marketDataClient } from '../../../api/marketdata/client.gen';
import type {
  CustomInstrumentResponse,
  InstrumentDetailsResponse,
  InstrumentSearchResult,
} from '../../../api/marketdata';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import type { AssetResponse } from '../../../api/portfolio';
import { AssetFormDialog, type AssetFormDialogData } from './asset-form-dialog';

// See auth.spec.ts: relative-import `vi.mock` is blocked, so this stubs `fetch` (what the
// generated client ultimately calls) instead of mocking the SDK module.
function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

function requestUrl(input: unknown): string {
  return typeof input === 'string' ? input : (input as Request).url;
}

const portfolioId = '22222222-2222-2222-2222-222222222222';
const instrumentId = '33333333-3333-3333-3333-333333333333';

const cashAsset: AssetResponse = {
  id: '11111111-1111-1111-1111-111111111111',
  portfolioId,
  assetClass: 0, // Cash
  valuationMode: 2, // CurrencyValued
  name: 'Checking account',
  currency: 'PLN',
  quantity: 0,
  transactionCount: 0,
};

const realEstateAsset: AssetResponse = {
  id: '55555555-5555-5555-5555-555555555555',
  portfolioId,
  assetClass: 7, // RealEstate
  valuationMode: 1, // Manual
  name: 'Apartment',
  currency: 'PLN',
  quantity: 0,
  manualValue: 650000,
  manualValueDate: '2020-06-15',
  transactionCount: 0,
};

const marketAsset: AssetResponse = {
  id: '44444444-4444-4444-4444-444444444444',
  portfolioId,
  assetClass: 2, // Stock
  valuationMode: 0, // Market
  name: 'Apple',
  currency: 'PLN',
  quantity: 10,
  instrumentId,
  transactionCount: 0,
};

const searchResult: InstrumentSearchResult = {
  id: instrumentId,
  ticker: 'AAPL.US',
  name: 'Apple Inc.',
  assetClass: 2,
  quoteCurrency: 'USD',
  verificationStatus: 0,
  lastPrice: 212,
  lastPriceDate: '2026-08-04',
};

const instrumentDetails: InstrumentDetailsResponse = {
  id: instrumentId,
  ticker: 'AAPL.US',
  name: 'Apple Inc.',
  assetClass: 2,
  quoteCurrency: 'USD',
  source: 1,
  verificationStatus: 0,
  lastPrice: 212,
  lastPriceDate: '2026-08-04',
};

describe('AssetFormDialog', () => {
  let fixture: ComponentFixture<AssetFormDialog>;
  let component: AssetFormDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(data: AssetFormDialogData): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch');

    await TestBed.configureTestingModule({
      imports: [AssetFormDialog],
      providers: [
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssetFormDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  describe('class drives the form', () => {
    it('defaults a new asset to Cash, which is currency-valued', async () => {
      await setup({ portfolioId });

      expect(component['isCurrencyValued']()).toBe(true);
      expect(component['isMarket']()).toBe(false);
      expect(component['isManual']()).toBe(false);
    });

    it('switches to manual fields for RealEstate', async () => {
      await setup({ portfolioId });

      component['form'].controls.assetClass.setValue(7); // RealEstate
      await fixture.whenStable();

      expect(component['isManual']()).toBe(true);
      expect(component['isCurrencyValued']()).toBe(false);
    });

    it('switches to the market/ticker path for Etf, and hides the custom-ticker link only for PreciousMetal', async () => {
      await setup({ portfolioId });

      component['form'].controls.assetClass.setValue(3); // Etf
      await fixture.whenStable();
      expect(component['isMarket']()).toBe(true);
      expect(component['customInstrumentAvailable']()).toBe(true);

      component['form'].controls.assetClass.setValue(6); // PreciousMetal
      await fixture.whenStable();
      expect(component['isMarket']()).toBe(true);
      expect(component['customInstrumentAvailable']()).toBe(false);
    });
  });

  describe('currency-valued (Cash/Deposit)', () => {
    it('creates Cash with just Name + Currency and closes with true on success', async () => {
      await setup({ portfolioId });
      fetchSpy.mockResolvedValue(jsonResponse(cashAsset, 201));

      component['form'].controls.name.setValue('Checking account');

      await component['onSubmit']();

      expect(fetchSpy).toHaveBeenCalledTimes(1);
      const request = fetchSpy.mock.calls[0][0] as Request;
      expect(request.method).toBe('POST');
      const body = await request.json();
      expect(body).toEqual({
        assetClass: 0,
        name: 'Checking account',
        currency: 'PLN',
        initialTransaction: null,
      });
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });
  });

  describe('manual valuation (e.g. RealEstate/Other)', () => {
    it('requires value + date and submits them', async () => {
      await setup({ portfolioId });
      fetchSpy.mockResolvedValue(jsonResponse(realEstateAsset, 201));

      component['form'].controls.assetClass.setValue(7); // RealEstate
      component['form'].controls.name.setValue('Apartment');
      component['form'].controls.manualValue.setValue(650000);
      await fixture.whenStable();

      await component['onSubmit']();

      expect(fetchSpy).toHaveBeenCalledTimes(1);
      const request = fetchSpy.mock.calls[0][0] as Request;
      const body = await request.json();
      expect(body.manualValue).toBe(650000);
      expect(body.manualValueDate).toBeTruthy();
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('does not submit when the value is missing', async () => {
      await setup({ portfolioId });
      fetchSpy.mockResolvedValue(jsonResponse(realEstateAsset, 201));

      component['form'].controls.assetClass.setValue(7);
      component['form'].controls.name.setValue('Apartment');
      component['form'].controls.manualValue.setValue(0);
      await fixture.whenStable();
      // manualValue = 0 is a valid (if unusual) non-negative number; simulate "missing" by clearing it.
      component['form'].controls.manualValue.setValue(null as unknown as number);

      await component['onSubmit']();

      expect(fetchSpy).not.toHaveBeenCalled();
    });

    it('pre-fills value and date from an existing manual asset', async () => {
      await setup({ portfolioId, asset: realEstateAsset });

      expect(component['form'].controls.manualValue.value).toBe(650000);
      expect(component['form'].controls.manualValueDate.value).toEqual(new Date(2020, 5, 15));
      expect(component['isManual']()).toBe(true);
    });
  });

  describe('market instruments', () => {
    it('refuses to submit without a verified/selected instrument', async () => {
      await setup({ portfolioId });

      component['form'].controls.assetClass.setValue(2); // Stock
      await fixture.whenStable();

      await component['onSubmit']();

      expect(fetchSpy).not.toHaveBeenCalled();
      expect(component['formError']()).toContain('Verify a ticker');
    });

    it('creates a market asset from an autocomplete selection, without copying the quote currency', async () => {
      await setup({ portfolioId });
      fetchSpy.mockResolvedValue(
        jsonResponse({ ...marketAsset, id: '66666666-6666-6666-6666-666666666666' }, 201),
      );

      component['form'].controls.assetClass.setValue(2); // Stock
      await fixture.whenStable();
      component['onInstrumentOptionSelected']({
        option: { value: searchResult },
      } as MatAutocompleteSelectedEvent);

      // Currency stays the user's own PLN/EUR/USD choice — the instrument's USD quote currency is
      // never copied in (ValueMarketAsset resolves FX off the instrument, not Asset.Currency).
      expect(component['form'].controls.currency.value).toBe('PLN');

      await component['onSubmit']();

      expect(fetchSpy).toHaveBeenCalledTimes(1);
      const request = fetchSpy.mock.calls[0][0] as Request;
      const body = await request.json();
      expect(body.instrumentId).toBe(instrumentId);
      expect(body.manualValue).toBeUndefined();
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('pre-fills market mode and the instrument picker when editing a market asset', async () => {
      fetchSpy = vi
        .spyOn(globalThis, 'fetch')
        .mockImplementation(async (input) =>
          requestUrl(input).includes('/instruments/')
            ? jsonResponse(instrumentDetails)
            : jsonResponse(marketAsset),
        );

      await TestBed.configureTestingModule({
        imports: [AssetFormDialog],
        providers: [
          provideNativeDateAdapter(),
          {
            provide: MAT_DIALOG_DATA,
            useValue: { portfolioId, asset: marketAsset } satisfies AssetFormDialogData,
          },
          { provide: MatDialogRef, useValue: (dialogRef = { close: vi.fn() }) },
        ],
      }).compileComponents();

      fixture = TestBed.createComponent(AssetFormDialog);
      component = fixture.componentInstance;
      await fixture.whenStable();

      expect(component['isMarket']()).toBe(true);
      expect(component['selectedInstrument']()).toEqual(instrumentDetails);
    });

    it('clears the selected instrument when the class changes away from market', async () => {
      fetchSpy = vi
        .spyOn(globalThis, 'fetch')
        .mockImplementation(async (input) =>
          requestUrl(input).includes('/instruments/')
            ? jsonResponse(instrumentDetails)
            : jsonResponse(marketAsset),
        );

      await TestBed.configureTestingModule({
        imports: [AssetFormDialog],
        providers: [
          provideNativeDateAdapter(),
          {
            provide: MAT_DIALOG_DATA,
            useValue: { portfolioId, asset: marketAsset } satisfies AssetFormDialogData,
          },
          { provide: MatDialogRef, useValue: (dialogRef = { close: vi.fn() }) },
        ],
      }).compileComponents();

      fixture = TestBed.createComponent(AssetFormDialog);
      component = fixture.componentInstance;
      await fixture.whenStable();
      expect(component['selectedInstrument']()).not.toBeNull();

      component['form'].controls.assetClass.setValue(7); // RealEstate — manual
      await fixture.whenStable();

      expect(component['selectedInstrument']()).toBeNull();
    });

    describe('verifying a custom ticker (ADR-018)', () => {
      async function setupOnStock(): Promise<void> {
        await setup({ portfolioId });
        component['form'].controls.assetClass.setValue(2); // Stock
        await fixture.whenStable();
        component['toggleCustomInstrumentForm']();
        component['customInstrumentForm'].setValue({
          ticker: 'MSFT.US',
          name: 'Microsoft Corp.',
          quoteCurrency: 'USD',
        });
      }

      it('Exists: adds and selects the instrument', async () => {
        const created: CustomInstrumentResponse = {
          id: '77777777-7777-7777-7777-777777777777',
          ticker: 'MSFT.US',
          name: 'Microsoft Corp.',
          source: 1,
          quoteCurrency: 'USD',
          assetClass: 2,
          verificationStatus: 0,
        };
        await setupOnStock();
        fetchSpy.mockResolvedValue(jsonResponse(created, 201));

        await component['submitCustomInstrument']();

        expect(component['selectedInstrument']()).toEqual(created);
        expect(component['showCustomInstrumentForm']()).toBe(false);
        const request = fetchSpy.mock.calls[0][0] as Request;
        const body = await request.json();
        expect(body).not.toHaveProperty('source');
        expect(body.assetClass).toBe(2);
      });

      it('DoesNotExist: blocks with a human-readable message, no instrument created', async () => {
        await setupOnStock();
        fetchSpy.mockResolvedValue(
          jsonResponse(
            {
              detail: "'NOPE.US' was not found at Stooq — check the ticker and try again.",
              errorCode: 'Validation.TickerNotFound',
            },
            400,
          ),
        );

        await component['submitCustomInstrument']();

        expect(component['customInstrumentOutcome']()).toBe('notFound');
        expect(component['customInstrumentMessage']()).toContain('was not found at Stooq');
        expect(component['selectedInstrument']()).toBeNull();
      });

      it('Unreachable: offers "add anyway", which creates the instrument Unverified', async () => {
        await setupOnStock();
        fetchSpy.mockResolvedValueOnce(
          jsonResponse(
            {
              detail:
                "Stooq could not be reached to verify 'MSFT.US' — try again shortly, or add it anyway.",
              errorCode: 'ServiceUnavailable.TickerVerificationUnreachable',
            },
            503,
          ),
        );

        await component['submitCustomInstrument']();

        expect(component['customInstrumentOutcome']()).toBe('unreachable');

        const unverified: CustomInstrumentResponse = {
          id: '88888888-8888-8888-8888-888888888888',
          ticker: 'MSFT.US',
          name: 'Microsoft Corp.',
          source: 1,
          quoteCurrency: 'USD',
          assetClass: 2,
          verificationStatus: 1, // Unverified
        };
        fetchSpy.mockResolvedValueOnce(jsonResponse(unverified, 201));

        // addInstrumentAnyway() is a fire-and-forget wrapper around submitCustomInstrument(true) (so
        // the template's (click) handler doesn't need to be async) — call the awaitable method
        // directly here for a deterministic test instead of racing fixture.whenStable().
        await component['submitCustomInstrument'](true);

        const secondRequest = fetchSpy.mock.calls[1][0] as Request;
        const body = await secondRequest.json();
        expect(body.allowUnverified).toBe(true);
        expect(component['selectedInstrument']()).toEqual(unverified);
      });

      it('Conflict: reports it already exists and points at search', async () => {
        await setupOnStock();
        fetchSpy.mockResolvedValue(
          jsonResponse(
            {
              detail: "An instrument with source 'Stooq' and ticker 'MSFT.US' already exists.",
              errorCode: 'Conflict.InstrumentAlreadyExists',
            },
            409,
          ),
        );

        await component['submitCustomInstrument']();

        expect(component['customInstrumentOutcome']()).toBe('conflict');
      });
    });
  });

  describe('add first transaction (create only)', () => {
    it('unchecked → submits initialTransaction: null', async () => {
      await setup({ portfolioId });
      fetchSpy.mockResolvedValue(jsonResponse(cashAsset, 201));

      component['form'].controls.name.setValue('Checking account');
      await component['onSubmit']();

      const request = fetchSpy.mock.calls[0][0] as Request;
      const body = await request.json();
      expect(body.initialTransaction).toBeNull();
    });

    it('checked → defaults to Deposit for a currency-valued class and submits it', async () => {
      await setup({ portfolioId });
      fetchSpy.mockResolvedValue(jsonResponse(cashAsset, 201));

      component['form'].controls.name.setValue('Checking account');
      component['toggleAddFirstTransaction']();
      expect(component['transactionForm'].controls.type.value).toBe(2); // Deposit
      component['transactionForm'].controls.quantity.setValue(1000);

      await component['onSubmit']();

      const request = fetchSpy.mock.calls[0][0] as Request;
      const body = await request.json();
      expect(body.initialTransaction).toEqual({
        type: 2,
        quantity: 1000,
        unitPrice: 1,
        fee: 0,
        date: expect.any(String),
      });
    });

    it('checked → defaults to Buy for a market class', async () => {
      await setup({ portfolioId });
      component['form'].controls.assetClass.setValue(2); // Stock
      await fixture.whenStable();

      component['toggleAddFirstTransaction']();

      expect(component['transactionForm'].controls.type.value).toBe(0); // Buy
      expect(component['transactionIsPriced']()).toBe(true);
    });

    it('is not shown / not sent when editing', async () => {
      await setup({ portfolioId, asset: cashAsset });

      fetchSpy.mockResolvedValue(jsonResponse(cashAsset));
      await component['onSubmit']();

      const request = fetchSpy.mock.calls[0][0] as Request;
      const body = await request.json();
      expect(body).not.toHaveProperty('initialTransaction');
      expect(request.method).toBe('PUT');
    });

    it('an invalid initial transaction blocks submission', async () => {
      await setup({ portfolioId });
      component['form'].controls.name.setValue('Checking account');
      component['toggleAddFirstTransaction']();
      component['transactionForm'].controls.quantity.setValue(-5);

      await component['onSubmit']();

      expect(fetchSpy).not.toHaveBeenCalled();
    });
  });

  describe('server error handling', () => {
    it('surfaces a validation error on the matching top-level field', async () => {
      await setup({ portfolioId });
      fetchSpy.mockResolvedValue(
        jsonResponse(
          {
            detail: 'Validation failed.',
            errors: { Name: ['The Name field is required.'] },
          },
          400,
        ),
      );

      component['form'].controls.name.setValue('Something');

      await component['onSubmit']();

      expect(component['form'].controls.name.hasError('server')).toBe(true);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('routes a nested InitialTransaction.* field error onto the transaction sub-form', async () => {
      await setup({ portfolioId });
      fetchSpy.mockResolvedValue(
        jsonResponse(
          {
            detail: 'Validation failed.',
            errors: { 'InitialTransaction.Quantity': ['The field Quantity must be non-negative.'] },
          },
          400,
        ),
      );

      component['form'].controls.name.setValue('Checking account');
      component['toggleAddFirstTransaction']();
      component['transactionForm'].controls.quantity.setValue(5);

      await component['onSubmit']();

      expect(component['transactionForm'].controls.quantity.hasError('server')).toBe(true);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('surfaces a generic server error as the form banner', async () => {
      await setup({ portfolioId });
      fetchSpy.mockResolvedValue(
        jsonResponse({ detail: 'Something odd happened.', errorCode: 'Unexpected.Whatever' }, 400),
      );

      component['form'].controls.name.setValue('Something');

      await component['onSubmit']();

      expect(component['formError']()).toBe('Something odd happened.');
      expect(dialogRef.close).not.toHaveBeenCalled();
    });
  });

  it('closes with false on cancel', async () => {
    await setup({ portfolioId });

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  it('REPRO: clicking the Market instrument toggle switches mode', async () => {
    await setup({ portfolioId });

    const toggles = Array.from(
      fixture.nativeElement.querySelectorAll('mat-button-toggle'),
    ) as HTMLElement[];
    console.log(
      'toggles before click:',
      toggles.map((t) => ({ text: t.textContent?.trim(), classes: t.className })),
    );

    const marketButton = toggles
      .find((t) => t.textContent?.includes('Market instrument'))
      ?.querySelector('button') as HTMLButtonElement;
    marketButton.click();
    await fixture.whenStable();

    console.log(
      'toggles after click:',
      toggles.map((t) => ({ text: t.textContent?.trim(), classes: t.className })),
    );
    console.log('mode signal after click:', component['mode']());

    expect(component['mode']()).toBe('market');
    const marketToggle = toggles.find((t) => t.textContent?.includes('Market instrument'))!;
    const manualToggle = toggles.find((t) => t.textContent?.includes('Manual valuation'))!;
    expect(marketToggle.className).toContain('mat-button-toggle-checked');
    expect(manualToggle.className).not.toContain('mat-button-toggle-checked');
  });

  it('pre-fills market mode and the instrument picker when editing a market asset', async () => {
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async (input) =>
        requestUrl(input).includes('/instruments/')
          ? jsonResponse(instrumentDetails)
          : jsonResponse(marketAsset),
      );

    await TestBed.configureTestingModule({
      imports: [AssetFormDialog],
      providers: [
        provideNativeDateAdapter(),
        {
          provide: MAT_DIALOG_DATA,
          useValue: { portfolioId, asset: marketAsset } satisfies AssetFormDialogData,
        },
        { provide: MatDialogRef, useValue: (dialogRef = { close: vi.fn() }) },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(AssetFormDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();

    expect(component['mode']()).toBe('market');
    expect(component['selectedInstrument']()).toEqual(instrumentDetails);
  });

  it('creates a market asset from an autocomplete selection', async () => {
    await setup({ portfolioId });
    fetchSpy.mockResolvedValue(
      jsonResponse({ ...marketAsset, id: '55555555-5555-5555-5555-555555555555' }, 201),
    );

    component['setMode']('market');
    component['onInstrumentOptionSelected']({
      option: { value: searchResult },
    } as MatAutocompleteSelectedEvent);

    expect(component['form'].controls.currency.value).toBe('USD');

    await component['onSubmit']();

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const request = fetchSpy.mock.calls[0][0] as Request;
    const body = await request.json();
    expect(body.instrumentId).toBe(instrumentId);
    expect(body.manualValue).toBeUndefined();
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('refuses to submit market mode without a selected instrument', async () => {
    await setup({ portfolioId });

    component['setMode']('market');
    await component['onSubmit']();

    expect(fetchSpy).not.toHaveBeenCalled();
    expect(component['formError']()).toContain('Pick an instrument');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('adds a custom instrument and selects it', async () => {
    const created: CustomInstrumentResponse = {
      id: '66666666-6666-6666-6666-666666666666',
      ticker: 'MSFT.US',
      name: 'Microsoft Corp.',
      source: 1,
      quoteCurrency: 'USD',
      assetClass: 2,
      verificationStatus: 0,
    };
    await setup({ portfolioId });
    fetchSpy.mockResolvedValue(jsonResponse(created, 201));

    component['setMode']('market');
    component['toggleCustomInstrumentForm']();
    component['customInstrumentForm'].setValue({
      source: 1,
      ticker: 'MSFT.US',
      name: 'Microsoft Corp.',
      quoteCurrency: 'USD',
      assetClass: 2,
    });

    await component['submitCustomInstrument']();

    expect(component['selectedInstrument']()).toEqual(created);
    expect(component['showCustomInstrumentForm']()).toBe(false);
    expect(component['form'].controls.currency.value).toBe('USD');
  });
});
