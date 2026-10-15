import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { restoreEnglish, translationValues } from '../../../../testing/i18n';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { jsonResponse, renderedText } from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  CashBankTransferDialog,
  type CashBankTransferDialogData,
} from './cash-bank-transfer-dialog';

const placeBalanceId = '11111111-1111-1111-1111-111111111111';
const bankMoneyId = '22222222-2222-2222-2222-222222222222';
const otherBankMoneyId = '33333333-3333-3333-3333-333333333333';

const placeAnchored: CashBankTransferDialogData = {
  direction: 'atm',
  anchor: 'place',
  anchorAssetId: placeBalanceId,
  anchorName: 'Safe · PLN',
  currency: 'PLN',
  anchorBalance: 0,
};

const bankAnchored: CashBankTransferDialogData = {
  direction: 'bankDeposit',
  anchor: 'bank',
  anchorAssetId: bankMoneyId,
  anchorName: 'mBank',
  currency: 'PLN',
  anchorBalance: 5000,
};

function account(name: string, currency: string, cashAssetId: string, isArchived = false) {
  return { id: `acc-${name}`, name, currency, cashAssetId, cashBalance: 5000, isArchived };
}

describe('CashBankTransferDialog', () => {
  let fixture: ComponentFixture<CashBankTransferDialog>;
  let component: CashBankTransferDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let writeResponse: () => Response;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    vi.useRealTimers();
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(data: CashBankTransferDialogData): Promise<void> {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 20));
    dialogRef = { close: vi.fn() };
    writeResponse = () => jsonResponse({ id: 'transfer' }, 201);
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      if (request.method !== 'GET') {
        return writeResponse();
      }
      if (request.url.includes('/transfer-candidates')) {
        return jsonResponse([{ assetId: placeBalanceId, name: 'Safe', balance: 700 }]);
      }
      return jsonResponse([
        account('mBank', 'PLN', bankMoneyId),
        account('mBank USD', 'USD', otherBankMoneyId),
        account('Old bank', 'PLN', '44444444-4444-4444-4444-444444444444', true),
      ]);
    });

    await TestBed.configureTestingModule({
      imports: [CashBankTransferDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CashBankTransferDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function writes(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((request: Request) => request.method !== 'GET');
  }

  function counterparts(): { assetId: string; name: string }[] {
    return component['counterparts'];
  }

  it('offers from a place only the unarchived bank accounts in its currency', async () => {
    await setup(placeAnchored);

    expect(counterparts().map((c) => c.name)).toEqual(['mBank']);
  });

  it('offers from a bank account the place balances in its currency', async () => {
    await setup(bankAnchored);

    const request = fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .find((candidate: Request) => candidate.url.includes('/transfer-candidates'));
    expect(request?.url).toContain('cashPlaces=true');
    expect(request?.url).toContain('currency=PLN');
    expect(counterparts().map((c) => c.name)).toEqual(['Safe · PLN']);
  });

  it('an ATM withdrawal from a place moves the bank money into it', async () => {
    await setup(placeAnchored);
    component['form'].patchValue({
      counterpartAssetId: bankMoneyId,
      amount: 500,
      date: new Date(2026, 9, 5),
    });

    await component['onSubmit']();

    const [request] = writes();
    expect(request.method).toBe('POST');
    expect(request.url).toContain('/api/portfolio/transfers');
    expect(await request.json()).toEqual({
      sourceAssetId: bankMoneyId,
      targetAssetId: placeBalanceId,
      amount: 500,
      date: '2026-10-05',
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('a bank deposit from a place moves its money into the bank', async () => {
    await setup({ ...placeAnchored, direction: 'bankDeposit' });
    component['form'].patchValue({
      counterpartAssetId: bankMoneyId,
      amount: 200,
      date: new Date(2026, 9, 6),
    });

    await component['onSubmit']();

    expect(await writes()[0].json()).toEqual({
      sourceAssetId: placeBalanceId,
      targetAssetId: bankMoneyId,
      amount: 200,
      date: '2026-10-06',
    });
  });

  it.each([
    ['atm', { source: bankMoneyId, target: placeBalanceId }],
    ['bankDeposit', { source: placeBalanceId, target: bankMoneyId }],
  ] as const)(
    'a %s opened from the bank side moves the money the same way',
    async (direction, ids) => {
      await setup({ ...bankAnchored, direction });
      component['form'].patchValue({
        counterpartAssetId: placeBalanceId,
        amount: 100,
        date: new Date(2026, 9, 7),
      });

      await component['onSubmit']();

      const body = await writes()[0].json();
      expect(body.sourceAssetId).toBe(ids.source);
      expect(body.targetAssetId).toBe(ids.target);
    },
  );

  it('blocks submit without a counterpart or an amount', async () => {
    await setup(placeAnchored);

    await component['onSubmit']();

    expect(component['form'].invalid).toBe(true);
    expect(writes()).toEqual([]);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('shows "Insufficient funds" inline on the amount and keeps the dialog open', async () => {
    await setup(placeAnchored);
    writeResponse = () =>
      jsonResponse(
        {
          title: 'Validation Failed',
          status: 400,
          detail: 'The balance would drop below zero.',
          errorCode: 'Validation.InsufficientFunds',
        },
        400,
      );
    component['form'].patchValue({
      counterpartAssetId: bankMoneyId,
      amount: 9000,
      date: new Date(2026, 9, 5),
    });

    await component['onSubmit']();
    fixture.detectChanges();
    await fixture.whenStable();

    expect(component['form'].controls.amount.getError('server')).toBe('Insufficient funds');
    expect(renderedText(fixture)).toContain('Insufficient funds');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('has the Polish inline message "Brak środków"', async () => {
    await setup(placeAnchored);

    expect(translationValues('pl')).toContain('Brak środków');
  });

  it('closes with false on cancel', async () => {
    await setup(placeAnchored);

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
