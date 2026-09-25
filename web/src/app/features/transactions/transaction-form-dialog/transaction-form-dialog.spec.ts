import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import type { TransactionResponse } from '../../../api/portfolio';
import { ASSET_CLASS } from '../../assets/asset-class';
import { TransactionFormDialog, type TransactionFormDialogData } from './transaction-form-dialog';

// See auth.spec.ts: relative-import `vi.mock` is blocked, so this stubs `fetch` (what the
// generated client ultimately calls) instead of mocking the SDK module.
function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const portfolioId = '22222222-2222-2222-2222-222222222222';
const assetId = '11111111-1111-1111-1111-111111111111';
// cash-transaction-types: the dialog knows its asset's class. A market class (ETF) keeps every
// transaction type, so the pre-existing cases below run against one.
const assetClass = ASSET_CLASS.Etf;

const existingTransaction: TransactionResponse = {
  id: '33333333-3333-3333-3333-333333333333',
  assetId,
  type: 0, // Buy
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

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(data: TransactionFormDialogData): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch');

    await TestBed.configureTestingModule({
      imports: [TransactionFormDialog],
      providers: [
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(TransactionFormDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  // Opens the Type select the way a user does and reads the options Material renders into the
  // overlay — what is offered, independent of how the dialog stores its list.
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
    return Array.from(TestBed.inject(OverlayContainer).getContainerElement().querySelectorAll('mat-option'), (option) =>
      (option.textContent ?? '').trim(),
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

    component['form'].controls.type.setValue(2); // Deposit
    component['form'].controls.quantity.setValue(500);

    expect(component['isPriced']()).toBe(false);
    expect(component['quantityLabel']()).toBe('Amount');

    await component['onSubmit']();

    const request = fetchSpy.mock.calls[0][0] as Request;
    const body = await request.json();
    expect(body.unitPrice).toBe(1);
    expect(body).not.toHaveProperty('fee');
  });

  // transactions-pln-value-and-fee-removal AC13: Fee is gone as a transaction type and as a field.
  // cash-transaction-types AC-8: run against a market class, which still offers all six types.
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

  // cash-transaction-types AC-8: a Cash asset accepts only Deposit/Withdraw (the backend answers
  // 400 to anything else), so the dialog offers only those two and starts on Deposit.
  it('offers only Deposit/Withdraw for a Cash asset', async () => {
    await setup({ portfolioId, assetId, assetClass: ASSET_CLASS.Cash });

    expect(component['form'].controls.type.value).toBe(2); // Deposit
    expect(await typeOptionLabels()).toEqual(['Deposit', 'Withdraw']);
  });

  it('offers all six types and starts on Buy for a market asset (ETF)', async () => {
    await setup({ portfolioId, assetId, assetClass: ASSET_CLASS.Etf });

    expect(component['form'].controls.type.value).toBe(0); // Buy
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
      transaction: { ...existingTransaction, type: 3 }, // Withdraw
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

    component['form'].controls.type.setValue(1); // Sell
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
});
