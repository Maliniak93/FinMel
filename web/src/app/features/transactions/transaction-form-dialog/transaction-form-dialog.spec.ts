import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { AbstractControl } from '@angular/forms';
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
    await setup({ assetId, assetClass });
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
    await setup({ assetId, assetClass, transaction: existingTransaction });
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

  it('saves an untouched quantity with its exact stored value', async () => {
    const transaction = { ...existingTransaction, quantity: 0.123456 };
    await setup({ assetId, assetClass, transaction });
    fetchSpy.mockResolvedValue(jsonResponse(transaction));

    await component['onSubmit']();

    const body = await (fetchSpy.mock.calls[0][0] as Request).json();
    expect(body.quantity).toBe(0.123456);
  });

  it('sends a typed quantity with more than two decimals unrounded', async () => {
    await setup({ assetId, assetClass });
    fetchSpy.mockResolvedValue(jsonResponse({ ...existingTransaction }, 201));

    component['form'].controls.quantity.setValue(0.123456);
    component['form'].controls.unitPrice.setValue(100);

    await component['onSubmit']();

    const body = await (fetchSpy.mock.calls[0][0] as Request).json();
    expect(body.quantity).toBe(0.123456);
  });

  it('sends unit price 1 for a Deposit, where the unit-price field is hidden', async () => {
    await setup({ assetId, assetClass: ASSET_CLASS.Cash });
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
    await setup({ assetId, assetClass });

    expect(await typeOptionLabels()).toEqual(['Buy', 'Sell', 'Dividend', 'Interest']);
    expect(Object.keys(component['form'].controls)).not.toContain('fee');

    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('[formcontrolname="fee"]')).toBeNull();
    const fieldLabels = Array.from(element.querySelectorAll('mat-label'), (label) =>
      (label.textContent ?? '').trim(),
    );
    expect(fieldLabels).not.toContain('Fee');
  });

  it('offers only Deposit/Withdraw for a Cash asset', async () => {
    await setup({ assetId, assetClass: ASSET_CLASS.Cash });

    expect(component['form'].controls.type.value).toBe(2);
    expect(await typeOptionLabels()).toEqual(['Deposit', 'Withdraw']);
  });

  it('offers only Buy/Sell for a precious metal and labels the quantity in pieces, with no price per piece', async () => {
    await setup({ assetId, assetClass: ASSET_CLASS.PreciousMetal, type: 0 });

    const fieldLabels = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('mat-label'),
      (label) => (label.textContent ?? '').trim(),
    );
    expect(fieldLabels).toContain('Pieces');
    expect(fieldLabels).not.toContain('Price per piece');
    expect(fieldLabels).not.toContain('Quantity');
    expect(await typeOptionLabels()).toEqual(['Buy', 'Sell']);
  });

  it('preselects the type from dialog data', async () => {
    await setup({ assetId, assetClass: ASSET_CLASS.Cash, type: 3 });

    expect(component['form'].controls.type.value).toBe(3);
  });

  it('offers Buy, Sell, Dividend and Interest and starts on Buy for a market asset (ETF)', async () => {
    await setup({ assetId, assetClass: ASSET_CLASS.Etf });

    expect(component['form'].controls.type.value).toBe(0);
    expect(await typeOptionLabels()).toEqual(['Buy', 'Sell', 'Dividend', 'Interest']);
  });

  it('keeps the stored type when editing a Cash asset transaction', async () => {
    await setup({
      assetId,
      assetClass: ASSET_CLASS.Cash,
      transaction: { ...existingTransaction, type: 3 },
    });

    expect(component['form'].controls.type.value).toBe(3);
  });

  it('does not submit an invalid form', async () => {
    await setup({ assetId, assetClass });
    fetchSpy.mockResolvedValue(jsonResponse(existingTransaction, 201));

    component['form'].controls.quantity.setValue(-1);

    await component['onSubmit']();

    expect(fetchSpy).not.toHaveBeenCalled();
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('surfaces a validation error on the matching field, without closing', async () => {
    await setup({ assetId, assetClass });
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
    await setup({ assetId, assetClass, transaction: existingTransaction });
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
    await setup({ assetId, assetClass });
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
    await setup({ assetId, assetClass });

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  it('shows labels and validation in Polish', async () => {
    await setup({ assetId, assetClass });
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
      'Fee (PLN)',
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
    await setup({ assetId, assetClass });
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
    await setup({ assetId, assetClass, transaction: existingTransaction });
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
  describe('Settlement source for a precious metal', () => {
    const metalAssetId = '44444444-4444-4444-4444-444444444444';
    const safeAssetId = '55555555-5555-5555-5555-555555555555';
    const mBankAssetId = '77777777-7777-7777-7777-777777777777';
    const metalData = {
      assetId: metalAssetId,
      assetClass: ASSET_CLASS.PreciousMetal,
    };

    function respond(request: Request): Response {
      if (request.url.includes('/transfer-candidates')) {
        return request.url.includes('cashPlaces=true')
          ? jsonResponse([{ assetId: safeAssetId, name: 'Sejf', balance: 2000 }])
          : jsonResponse([{ assetId: mBankAssetId, name: 'mBank', balance: 5000 }]);
      }

      return jsonResponse({ ...existingTransaction, assetId: metalAssetId }, 201);
    }

    function respondWithError(errorCode: string): (request: Request) => Response {
      return (request) =>
        request.method === 'POST'
          ? jsonResponse({ detail: errorCode, errorCode }, 400)
          : jsonResponse([{ assetId: safeAssetId, name: 'Sejf', balance: 2000 }]);
    }

    function totalAmount(): AbstractControl {
      return component['form'].get('totalAmount') as AbstractControl;
    }

    function postedRequests(): Request[] {
      return fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .filter((request: Request) => request.method === 'POST');
    }

    function cashSelect(): HTMLElement | null {
      return (fixture.nativeElement as HTMLElement).querySelector(
        'mat-select[formcontrolname="cashAssetId"]',
      );
    }

    it('for a Buy shows quantity, the invoice total and a required "Rozlicz z" grouped into Gotówka and Konta bankowe', async () => {
      await setup(metalData, respond);
      await switchLanguage(fixture, 'pl');
      fixture.detectChanges();
      await fixture.whenStable();

      expect(labelsOf(fixture.nativeElement, 'mat-label')).toEqual(
        expect.arrayContaining(['Kwota z faktury (PLN)', 'Rozlicz z']),
      );
      expect(component['form'].controls.cashAssetId.valid).toBe(false);

      const trigger = cashSelect()?.querySelector<HTMLElement>('.mat-mdc-select-trigger');
      trigger?.click();
      fixture.detectChanges();
      await fixture.whenStable();
      const groups = Array.from(
        TestBed.inject(OverlayContainer).getContainerElement().querySelectorAll('mat-optgroup'),
        (group) => (group.textContent ?? '').replace(/\s+/g, ' ').trim(),
      );
      expect(groups).toHaveLength(2);
      expect(groups[0]).toContain('Gotówka');
      expect(groups[0]).toContain('Sejf');
      expect(groups[0]).toContain('2000,00');
      expect(groups[1]).toContain('Konta bankowe');
      expect(groups[1]).toContain('mBank');
      expect(groups[1]).toContain('5000,00');
    });

    it('sends the chosen cashAssetId and the invoice total, not a unit price', async () => {
      await setup(metalData, respond);
      fixture.detectChanges();
      await fixture.whenStable();

      component['form'].controls.quantity.setValue(2);
      totalAmount().setValue(2400);
      component['form'].controls.cashAssetId.setValue(safeAssetId);

      await component['onSubmit']();

      const [post] = postedRequests();
      expect(post.url).toContain('/transactions');
      const body = await post.json();
      expect(body).toMatchObject({ quantity: 2, cashAssetId: safeAssetId, totalAmount: 2400 });
      expect(body).not.toHaveProperty('unitPrice');
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('does not submit while no "Rozlicz z" account is chosen', async () => {
      await setup(metalData, respond);
      fixture.detectChanges();
      await fixture.whenStable();
      component['form'].controls.quantity.setValue(1);
      totalAmount().setValue(1200);

      await component['onSubmit']();

      expect(postedRequests()).toEqual([]);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('shows "Brak środków" when the settlement source cannot cover the total', async () => {
      await setup(metalData, respondWithError('Validation.InsufficientFunds'));
      await switchLanguage(fixture, 'pl');
      component['form'].controls.quantity.setValue(1);
      totalAmount().setValue(1200);
      component['form'].controls.cashAssetId.setValue(safeAssetId);

      await component['onSubmit']();

      expect(component['formError']()).toContain('Brak środków');
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('shows "Różne waluty" when the settlement source is in another currency', async () => {
      await setup(metalData, respondWithError('Validation.TransferCurrencyMismatch'));
      await switchLanguage(fixture, 'pl');
      component['form'].controls.quantity.setValue(1);
      totalAmount().setValue(1200);
      component['form'].controls.cashAssetId.setValue(safeAssetId);

      await component['onSubmit']();

      expect(component['formError']()).toContain('Różne waluty');
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('has no Cash select for a Cash asset', async () => {
      await setup({ assetId, assetClass: ASSET_CLASS.Cash, currency: 'EUR' }, respond);
      fixture.detectChanges();
      await fixture.whenStable();
      expect(cashSelect()).toBeNull();
    });
  });

  describe('Brokerage trade for a stock or ETF', () => {
    const etfAssetId = '66666666-6666-6666-6666-666666666666';
    const accountId = '77777777-7777-7777-7777-777777777777';
    const etfData: TransactionFormDialogData = {
      assetId: etfAssetId,
      assetClass: ASSET_CLASS.Etf,
      currency: 'PLN',
      accountId,
    };

    function respond(request: Request): Response {
      if (request.url.includes(`/accounts/${accountId}`)) {
        return jsonResponse({
          id: accountId,
          name: 'XTB IKE',
          type: 0,
          wrapper: 1,
          isArchived: false,
          cashAssetId: '88888888-8888-8888-8888-888888888888',
          cashBalance: 5000,
        });
      }
      return jsonResponse({ ...existingTransaction, assetId: etfAssetId }, 201);
    }

    async function render(data: TransactionFormDialogData): Promise<void> {
      await setup(data, respond);
      fixture.detectChanges();
      await fixture.whenStable();
    }

    function element(): HTMLElement {
      return fixture.nativeElement as HTMLElement;
    }

    function postedTransaction(): Request | undefined {
      return fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .find(
          (request: Request) => request.method === 'POST' && request.url.includes('/transactions'),
        );
    }

    async function submitRejected(errorCode: string): Promise<void> {
      await render(etfData);
      fetchSpy.mockResolvedValue(jsonResponse({ detail: 'Rejected.', errorCode }, 400));
      component['form'].controls.quantity.setValue(10);
      component['form'].controls.unitPrice.setValue(120);
      await switchLanguage(fixture, 'pl');
      await component['onSubmit']();
    }

    it('Buy shows a fee field and the account-cash hint, has no cash select, and sends feeAmount', async () => {
      await render(etfData);

      component['form'].controls.quantity.setValue(10);
      component['form'].controls.unitPrice.setValue(120);
      component['form'].controls.feeAmount.setValue(3);
      fixture.detectChanges();
      await fixture.whenStable();

      expect(element().querySelector('mat-select[formcontrolname="cashAssetId"]')).toBeNull();
      expect(element().querySelector('[formcontrolname="feeAmount"]')).not.toBeNull();
      const text = element().textContent ?? '';
      expect(text).toContain('XTB IKE');
      expect(text).toContain('1,203.00');
      expect(text).toContain('5,000.00');

      await component['onSubmit']();

      const body = await postedTransaction()!.json();
      expect(body.feeAmount).toBe(3);
      expect(body.cashAssetId ?? null).toBeNull();
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('does not request the free Cash accounts of a stock or ETF', async () => {
      await render(etfData);

      expect(
        fetchSpy.mock.calls.some((call: unknown[]) =>
          (call[0] as Request).url.includes('/cash-accounts'),
        ),
      ).toBe(false);
    });

    it('labels the fee field "Prowizja (PLN)" in Polish', async () => {
      await render(etfData);

      await switchLanguage(fixture, 'pl');

      expect(labelsOf(element(), 'mat-label')).toContain('Prowizja (PLN)');
    });

    it('Dividend shows one "Kwota netto (PLN)" field and no fee, price or cash select', async () => {
      await render({ ...etfData, type: 4 });

      await switchLanguage(fixture, 'pl');

      expect(labelsOf(element(), 'mat-label')).toContain('Kwota netto (PLN)');
      expect(element().querySelector('[formcontrolname="feeAmount"]')).toBeNull();
      expect(element().querySelector('[formcontrolname="unitPrice"]')).toBeNull();
      expect(element().querySelector('mat-select[formcontrolname="cashAssetId"]')).toBeNull();

      component['form'].controls.quantity.setValue(37.4);
      await component['onSubmit']();

      const body = await postedTransaction()!.json();
      expect(body.quantity).toBe(37.4);
      expect(body.unitPrice).toBe(1);
      expect(body.feeAmount ?? null).toBeNull();
    });

    it('shows "Brak środków na rachunku" for Validation.InsufficientFunds', async () => {
      await submitRejected('Validation.InsufficientFunds');

      expect(component['formError']()).toBe('Brak środków na rachunku');
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('shows "Brak kursu waluty z tego dnia" for Validation.FxRateMissing', async () => {
      await submitRejected('Validation.FxRateMissing');

      expect(component['formError']()).toBe('Brak kursu waluty z tego dnia');
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('pre-fills the fee when editing a Buy and sends it back', async () => {
      const buy: TransactionResponse = {
        ...existingTransaction,
        assetId: etfAssetId,
        currency: 'PLN',
        feeAmount: 3,
      };
      await render({ ...etfData, transaction: buy });
      fetchSpy.mockResolvedValue(jsonResponse(buy));

      expect(component['form'].controls.feeAmount.value).toBe(3);

      component['form'].controls.feeAmount.setValue(4);
      await component['onSubmit']();

      const put = fetchSpy.mock.calls
        .map((call: unknown[]) => call[0] as Request)
        .find((request: Request) => request.method === 'PUT');
      const body = await put!.json();
      expect(body.feeAmount).toBe(4);
    });

    describe('pays in PLN', () => {
      const eurData: TransactionFormDialogData = { ...etfData, currency: 'EUR' };

      function toggle(): Element | null {
        return element().querySelector('[formcontrolname="paymentCurrency"]');
      }

      async function settle(): Promise<void> {
        fixture.detectChanges();
        await fixture.whenStable();
      }

      it('pays in PLN: shows the per-unit hint and the account-cash hint and posts amountPln', async () => {
        await render(eurData);
        component['form'].controls.quantity.setValue(10);
        component['form'].controls.unitPrice.setValue(100);
        component['form'].controls.feeAmount.setValue(5);
        await settle();

        expect(toggle()).not.toBeNull();
        expect(element().querySelector('[formcontrolname="amountPln"]')).toBeNull();

        component['form'].controls.paymentCurrency.setValue('PLN');
        await settle();
        component['form'].controls.amountPln.setValue(4312.5);
        await settle();

        const text = element().textContent ?? '';
        expect(text).toContain('431.25');
        expect(text).toContain('4.3125');
        expect(text).toContain('4,317.50');

        await component['onSubmit']();

        const body = await postedTransaction()!.json();
        expect(body.amountPln).toBe(4312.5);
        expect(body.feeAmount).toBe(5);
        expect(dialogRef.close).toHaveBeenCalledWith(true);
      });

      it('pays in PLN: requires the amount once PLN is chosen', async () => {
        await render(eurData);
        component['form'].controls.quantity.setValue(10);
        component['form'].controls.unitPrice.setValue(100);
        component['form'].controls.paymentCurrency.setValue('PLN');
        await settle();

        await component['onSubmit']();

        expect(postedTransaction()).toBeUndefined();
        expect(dialogRef.close).not.toHaveBeenCalled();
      });

      it('pays in PLN: sends no amountPln while the asset currency is chosen', async () => {
        await render(eurData);
        component['form'].controls.quantity.setValue(10);
        component['form'].controls.unitPrice.setValue(100);

        await component['onSubmit']();

        const body = await postedTransaction()!.json();
        expect(body.amountPln ?? null).toBeNull();
      });

      it('pays in PLN: has no toggle for a PLN asset', async () => {
        await render(etfData);

        expect(toggle()).toBeNull();
      });

      it('pays in PLN: has no toggle for a Dividend', async () => {
        await render({ ...eurData, type: 4 });

        expect(toggle()).toBeNull();
      });

      it('pays in PLN: editing a transaction with amountPln opens on PLN with the amount filled', async () => {
        const buy: TransactionResponse = {
          ...existingTransaction,
          assetId: etfAssetId,
          currency: 'EUR',
          valuePln: 4312.5,
          amountPln: 4312.5,
        };
        await render({ ...eurData, transaction: buy });

        expect(component['form'].controls.paymentCurrency.value).toBe('PLN');
        expect(component['form'].controls.amountPln.value).toBe(4312.5);
        expect(element().querySelector('[formcontrolname="amountPln"]')).not.toBeNull();
      });

      it('pays in PLN: shows the toggle and field labels in Polish', async () => {
        await render(eurData);
        component['form'].controls.paymentCurrency.setValue('PLN');
        await settle();

        await switchLanguage(fixture, 'pl');

        const text = element().textContent ?? '';
        expect(text).toContain('Waluta zapłaty');
        expect(labelsOf(element(), 'mat-label')).toContain('Kwota w PLN');
      });
    });
  });
});
