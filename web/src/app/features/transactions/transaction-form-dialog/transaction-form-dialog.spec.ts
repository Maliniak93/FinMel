import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { TranslocoService } from '@jsverse/transloco';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import type { TransactionResponse } from '../../../api/portfolio';
import { ASSET_CLASS } from '../../assets/asset-class';
import { TransactionFormDialog, type TransactionFormDialogData } from './transaction-form-dialog';
import { provideI18nTesting } from '../../../core/i18n/testing';
import {
  labelsOf,
  polishProblems,
  restoreEnglish,
  switchLanguage,
  TRANSLATIONS,
} from '../../../../testing/i18n';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const portfolioId = '22222222-2222-2222-2222-222222222222';
const assetId = '11111111-1111-1111-1111-111111111111';
const assetClass = ASSET_CLASS.Etf;

const existingTransaction: TransactionResponse = {
  id: '33333333-3333-3333-3333-333333333333',
  assetId,
  type: 0,
  quantity: 10,
  unitPrice: 100,
  currency: 'EUR',
  valuePln: 4300,
  date: '2024-01-15',
};

describe('TransactionFormDialog', () => {
  let fixture: ComponentFixture<TransactionFormDialog>;
  let component: TransactionFormDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(
    data: TransactionFormDialogData,
    respond?: (request: Request) => Response,
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch');
    if (respond) {
      fetchSpy.mockImplementation(async (input: RequestInfo | URL) => respond(input as Request));
    }

    await TestBed.configureTestingModule({
      imports: [TransactionFormDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TransactionFormDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  async function typeOptionLabels(): Promise<string[]> {
    const trigger = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      'mat-select[formcontrolname="type"] .mat-mdc-select-trigger',
    );
    if (!trigger) {
      throw new Error('No Type select rendered.');
    }
    trigger.click();
    fixture.detectChanges();
    await fixture.whenStable();
    return Array.from(
      TestBed.inject(OverlayContainer).getContainerElement().querySelectorAll('mat-option'),
      (option) => (option.textContent ?? '').trim(),
    );
  }

  it('creates a Buy with the form values and closes with true on success', async () => {
    await setup({ portfolioId, assetId, assetClass });
    fetchSpy.mockResolvedValue(jsonResponse({ ...existingTransaction }, 201));

    component['form'].controls.quantity.setValue(10);
    component['form'].controls.unitPrice.setValue(100);

    await component['onSubmit']();

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(request.method).toBe('POST');
    expect(request.url).toContain(`/assets/${assetId}/transactions`);
    const body = await request.json();
    expect(body.unitPrice).toBe(100);
    expect(body).not.toHaveProperty('fee');
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('pre-fills the form from an existing transaction and updates via PUT on success', async () => {
    await setup({ portfolioId, assetId, assetClass, transaction: existingTransaction });
    fetchSpy.mockResolvedValue(jsonResponse(existingTransaction));

    expect(component['form'].controls.type.value).toBe(0);
    expect(component['form'].controls.quantity.value).toBe(10);
    expect(component['form'].controls.unitPrice.value).toBe(100);
    expect(component['form'].controls.date.value).toEqual(new Date(2024, 0, 15));

    await component['onSubmit']();

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(request.method).toBe('PUT');
    expect(request.url).toContain(existingTransaction.id);
    const body = await request.json();
    expect(body.date).toBe('2024-01-15');
    expect(body).not.toHaveProperty('fee');
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('sends unit price 1 for a Deposit, where the unit-price field is hidden', async () => {
    await setup({ portfolioId, assetId, assetClass });
    fetchSpy.mockResolvedValue(jsonResponse({ ...existingTransaction, type: 2 }, 201));

    component['form'].controls.type.setValue(2);
    component['form'].controls.quantity.setValue(500);

    expect(component['isPriced']()).toBe(false);
    expect(TestBed.inject(TranslocoService).translate(component['quantityLabel']())).toBe('Amount');

    await component['onSubmit']();

    const request = fetchSpy.mock.calls[0][0] as Request;
    const body = await request.json();
    expect(body.unitPrice).toBe(1);
    expect(body).not.toHaveProperty('fee');
  });

  it('offers no Fee type', async () => {
    await setup({ portfolioId, assetId, assetClass });

    expect(await typeOptionLabels()).toEqual([
      'Buy',
      'Sell',
      'Deposit',
      'Withdraw',
      'Dividend',
      'Interest',
    ]);
    expect(Object.keys(component['form'].controls)).not.toContain('fee');

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[formcontrolname="fee"]')).toBeNull();
    const fieldLabels = Array.from(element.querySelectorAll('mat-label'), (label) =>
      (label.textContent ?? '').trim(),
    );
    expect(fieldLabels).not.toContain('Fee');
  });

  it('offers only Deposit/Withdraw for a Cash asset', async () => {
    await setup({ portfolioId, assetId, assetClass: ASSET_CLASS.Cash });

    expect(component['form'].controls.type.value).toBe(2);
    expect(await typeOptionLabels()).toEqual(['Deposit', 'Withdraw']);
  });

  it('offers only Buy/Sell for a precious metal and labels the quantity in pieces and the price per piece', async () => {
    await setup({ portfolioId, assetId, assetClass: ASSET_CLASS.PreciousMetal, type: 0 });

    const fieldLabels = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('mat-label'),
      (label) => (label.textContent ?? '').trim(),
    );
    expect(fieldLabels).toContain('Pieces');
    expect(fieldLabels).toContain('Price per piece');
    expect(fieldLabels).not.toContain('Quantity');
    expect(await typeOptionLabels()).toEqual(['Buy', 'Sell']);
  });

  it('preselects the type from dialog data', async () => {
    await setup({ portfolioId, assetId, assetClass: ASSET_CLASS.Cash, type: 3 });

    expect(component['form'].controls.type.value).toBe(3);
  });

  it('offers all six types and starts on Buy for a market asset (ETF)', async () => {
    await setup({ portfolioId, assetId, assetClass: ASSET_CLASS.Etf });

    expect(component['form'].controls.type.value).toBe(0);
    expect(await typeOptionLabels()).toEqual([
      'Buy',
      'Sell',
      'Deposit',
      'Withdraw',
      'Dividend',
      'Interest',
    ]);
  });

  it('keeps the stored type when editing a Cash asset transaction', async () => {
    await setup({
      portfolioId,
      assetId,
      assetClass: ASSET_CLASS.Cash,
      transaction: { ...existingTransaction, type: 3 },
    });

    expect(component['form'].controls.type.value).toBe(3);
  });

  it('does not submit an invalid form', async () => {
    await setup({ portfolioId, assetId, assetClass });
    fetchSpy.mockResolvedValue(jsonResponse(existingTransaction, 201));

    component['form'].controls.quantity.setValue(-1);

    await component['onSubmit']();

    expect(fetchSpy).not.toHaveBeenCalled();
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('surfaces a validation error on the matching field, without closing', async () => {
    await setup({ portfolioId, assetId, assetClass });
    fetchSpy.mockResolvedValue(
      jsonResponse(
        {
          detail: 'Validation failed.',
          errors: { UnitPrice: ['The UnitPrice field must be a non-negative number.'] },
        },
        400,
      ),
    );

    component['form'].controls.quantity.setValue(5);

    await component['onSubmit']();

    expect(component['form'].controls.unitPrice.hasError('server')).toBe(true);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('surfaces the 409 history-breaking-edit conflict as a human-readable form banner', async () => {
    await setup({ portfolioId, assetId, assetClass, transaction: existingTransaction });
    fetchSpy.mockResolvedValue(
      jsonResponse(
        {
          detail:
            'This change would make a later Sell take the asset quantity below zero (selling more than the position at some point in history).',
          errorCode: 'Conflict.OversellsPosition',
        },
        409,
      ),
    );

    await component['onSubmit']();

    expect(component['formError']()).toBe(
      'This change would make a later Sell take the asset quantity below zero (selling more than the position at some point in history).',
    );
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('surfaces the 400 oversell validation error as a human-readable form banner', async () => {
    await setup({ portfolioId, assetId, assetClass });
    fetchSpy.mockResolvedValue(
      jsonResponse(
        {
          detail:
            'This Sell would take the asset quantity below zero (selling more than the position).',
          errorCode: 'Validation.OversellsPosition',
        },
        400,
      ),
    );

    component['form'].controls.type.setValue(1);
    component['form'].controls.quantity.setValue(999);

    await component['onSubmit']();

    expect(component['formError']()).toBe(
      'This Sell would take the asset quantity below zero (selling more than the position).',
    );
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with false on cancel', async () => {
    await setup({ portfolioId, assetId, assetClass });

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  it('shows labels and validation in Polish', async () => {
    await setup({ portfolioId, assetId, assetClass });
    const element = fixture.nativeElement as HTMLElement;
    component['form'].controls.quantity.setValue(-1);
    component['form'].controls.unitPrice.setValue(-1);
    component['form'].controls.date.setValue(null as unknown as Date);
    await component['onSubmit']();
    fixture.detectChanges();
    await fixture.whenStable();
    const texts = () => [
      ...labelsOf(element, 'h2'),
      ...labelsOf(element, 'mat-label'),
      ...labelsOf(element, 'mat-error'),
      ...labelsOf(element, 'mat-dialog-actions button'),
    ];

    const english = texts();
    expect(english).toEqual([
      'New transaction',
      'Type',
      'Quantity',
      'Unit price',
      'Date',
      'Quantity must not be negative.',
      'Unit price must not be negative.',
      'Date is required.',
      'Cancel',
      'Record',
    ]);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(english, texts())).toEqual([]);
  });

  it('names the amount field in the Polish validation message of a non-priced type', async () => {
    await setup({ portfolioId, assetId, assetClass });
    const element = fixture.nativeElement as HTMLElement;
    component['form'].controls.type.setValue(2);
    component['form'].controls.quantity.setValue(-1);
    await component['onSubmit']();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(labelsOf(element, 'mat-error')).toEqual(['Amount must not be negative.']);

    await switchLanguage(fixture, 'pl');

    const [message] = labelsOf(element, 'mat-error');
    expect(polishProblems(['Amount must not be negative.'], [message])).toEqual([]);
    expect(message).toContain(TRANSLATIONS.pl['enums.quantityField.amount'] as string);
  });

  it('shows the edit title and save button in Polish', async () => {
    await setup({ portfolioId, assetId, assetClass, transaction: existingTransaction });
    const element = fixture.nativeElement as HTMLElement;
    const texts = () => [
      ...labelsOf(element, 'h2'),
      ...labelsOf(element, 'mat-dialog-actions button'),
    ];

    const english = texts();
    expect(english).toEqual(['Edit transaction', 'Cancel', 'Save']);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(english, texts())).toEqual([]);
  });
  describe('Cash account for a precious metal', () => {
    const metalAssetId = '44444444-4444-4444-4444-444444444444';
    const cashAssetId = '55555555-5555-5555-5555-555555555555';
    const metalData = {
      portfolioId,
      assetId: metalAssetId,
      assetClass: ASSET_CLASS.PreciousMetal,
    };

    function respond(request: Request): Response {
      return request.url.includes('/transfer-candidates')
        ? jsonResponse([
            {
              assetId: cashAssetId,
              portfolioId,
              name: 'Wallet',
              portfolioName: 'Main',
              balance: 5000,
            },
          ])
        : jsonResponse({ ...existingTransaction, assetId: metalAssetId }, 201);
    }

    function cashSelect(): HTMLElement | null {
      return (fixture.nativeElement as HTMLElement).querySelector(
        'mat-select[formcontrolname="cashAssetId"]',
      );
    }

    it('sends the chosen cashAssetId and shows quantity times price as a hint', async () => {
      await setup(metalData, respond);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(cashSelect()).not.toBeNull();
      const candidatesRequest = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .find((request: Request) => request.url.includes('/transfer-candidates'));
      expect(candidatesRequest?.url).toContain('assetClass=');

      component['form'].controls.quantity.setValue(2);
      component['form'].controls.unitPrice.setValue(1200);
      component['form'].controls.cashAssetId.setValue(cashAssetId);
      fixture.detectChanges();
      await fixture.whenStable();

      expect((fixture.nativeElement as HTMLElement).textContent).toContain('2,400');

      await component['onSubmit']();

      const post = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .find(
          (request: Request) => request.method === 'POST' && request.url.includes('/transactions'),
        );
      expect(post).toBeDefined();
      const body = await post!.json();
      expect(body.cashAssetId).toBe(cashAssetId);
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('omits cashAssetId while no account is chosen', async () => {
      await setup(metalData, respond);
      fixture.detectChanges();
      await fixture.whenStable();
      component['form'].controls.quantity.setValue(1);
      component['form'].controls.unitPrice.setValue(1200);

      await component['onSubmit']();

      const post = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .find(
          (request: Request) => request.method === 'POST' && request.url.includes('/transactions'),
        );
      const body = await post!.json();
      expect(body.cashAssetId ?? null).toBeNull();
    });

    it('has no Cash select for a Stock or for a Dividend', async () => {
      await setup({ portfolioId, assetId, assetClass: ASSET_CLASS.Stock }, respond);
      fixture.detectChanges();
      await fixture.whenStable();
      expect(cashSelect()).toBeNull();
      fixture.destroy();
      fetchSpy.mockRestore();
      TestBed.resetTestingModule();

      await setup({ portfolioId, assetId, assetClass: ASSET_CLASS.Etf, type: 4 }, respond);
      fixture.detectChanges();
      await fixture.whenStable();
      expect(cashSelect()).toBeNull();
    });
  });
});
