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

const existingAsset: AssetResponse = {
  id: '11111111-1111-1111-1111-111111111111',
  portfolioId,
  assetClass: 2,
  name: 'Apple',
  currency: 'USD',
  quantity: 10,
  manualValue: 1000,
  manualValueDate: '2020-06-15',
  transactionCount: 0,
};

const marketAsset: AssetResponse = {
  id: '44444444-4444-4444-4444-444444444444',
  portfolioId,
  assetClass: 2,
  name: 'Apple',
  currency: 'USD',
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

  it('creates with the form values and closes with true on success', async () => {
    await setup({ portfolioId });
    fetchSpy.mockResolvedValue(jsonResponse({ ...existingAsset, name: 'New asset' }, 201));

    component['form'].controls.name.setValue('New asset');
    component['form'].controls.manualValue.setValue(500);

    await component['onSubmit']();

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(request.method).toBe('POST');
    expect(request.url).toContain(`/portfolios/${portfolioId}/assets`);
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('pre-fills the form from an existing asset and updates via PUT on success', async () => {
    await setup({ portfolioId, asset: existingAsset });
    fetchSpy.mockResolvedValue(jsonResponse(existingAsset));

    expect(component['form'].controls.name.value).toBe('Apple');
    expect(component['form'].controls.currency.value).toBe('USD');
    expect(component['form'].controls.manualValue.value).toBe(1000);
    expect(component['form'].controls.manualValueDate.value).toEqual(new Date(2020, 5, 15));

    await component['onSubmit']();

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(request.method).toBe('PUT');
    expect(request.url).toContain(existingAsset.id);
    const body = await request.json();
    expect(body.manualValueDate).toBe('2020-06-15');
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('does not submit an invalid form', async () => {
    await setup({ portfolioId });
    fetchSpy.mockResolvedValue(jsonResponse(existingAsset, 201));

    component['form'].controls.name.setValue('');

    await component['onSubmit']();

    expect(fetchSpy).not.toHaveBeenCalled();
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('surfaces a validation error on the matching field, without closing', async () => {
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

  it('surfaces a generic server error as the form banner', async () => {
    await setup({ portfolioId });
    fetchSpy.mockResolvedValue(
      jsonResponse(
        { detail: 'Amount must not be negative.', errorCode: 'Money.NegativeAmount' },
        400,
      ),
    );

    component['form'].controls.name.setValue('Something');

    await component['onSubmit']();

    expect(component['formError']()).toBe('Amount must not be negative.');
    expect(dialogRef.close).not.toHaveBeenCalled();
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
