import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { jsonResponse } from '../../assets/asset-form/testing/asset-form-fixtures';
import { SettleExpenseDialog } from './settle-expense-dialog';

const plnAccountId = '11111111-1111-1111-1111-111111111111';
const eurAccountId = '22222222-2222-2222-2222-222222222222';
const otherPlnAccountId = '44444444-4444-4444-4444-444444444444';
const archivedAccountId = '33333333-3333-3333-3333-333333333333';

function account(id: string, name: string, currency: string, isArchived = false) {
  return {
    id,
    name,
    type: 2,
    wrapper: 0,
    currency,
    isArchived,
    cashAssetId: `cash-${id}`,
    cashBalance: 0,
    outsideNetWorth: false,
  };
}

const payment = {
  recurringExpenseId: 'expense-1',
  nominalMonth: '2026-10',
  date: '2026-10-05',
  name: 'Czynsz',
  amount: 2400,
  status: 'Pending',
  accountId: plnAccountId,
  accountName: 'mBank ROR',
  plannedAccountId: plnAccountId,
  currency: 'PLN',
  actualAmount: null,
  actualDate: null,
  actualAmountPln: null,
  difference: null,
  withdrawalCreated: false,
};

const oneOff = {
  id: 'one-off-expense-1',
  name: 'Abonament',
  date: '2026-10-03',
  amount: 100,
  inMonth: true,
  accountId: null,
  accountName: null,
  status: 'Pending',
  currency: null,
  actualAmount: null,
  actualDate: null,
  actualAmountPln: null,
  difference: null,
  withdrawalCreated: false,
};

describe('SettleExpenseDialog', () => {
  let fixture: ComponentFixture<SettleExpenseDialog>;
  let component: SettleExpenseDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 20, 12));
  });

  afterEach(() => {
    vi.useRealTimers();
    fetchSpy.mockRestore();
  });

  async function setup(
    data: Record<string, unknown>,
    putResponse: () => Response = () => new Response(null, { status: 204 }),
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      if (request.method === 'PUT') {
        return putResponse();
      }
      return jsonResponse([
        account(plnAccountId, 'mBank ROR', 'PLN'),
        account(otherPlnAccountId, 'Rezerwa', 'PLN'),
        account(eurAccountId, 'Revolut EUR', 'EUR'),
        account(archivedAccountId, 'Stare konto', 'PLN', true),
      ]);
    });

    await TestBed.configureTestingModule({
      imports: [SettleExpenseDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SettleExpenseDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function puts(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((request: Request) => request.method === 'PUT');
  }

  function submitButton(): HTMLButtonElement {
    return element().querySelector<HTMLButtonElement>('button[type="submit"]')!;
  }

  function field(name: string): HTMLElement | null {
    return element().querySelector<HTMLElement>(`[formControlName="${name}"]`);
  }

  it('defaults', async () => {
    await setup({ payment });

    const values = component['form'].getRawValue();
    expect(values.actualAmount).toBe(2400);
    expect(values.accountId).toBe(plnAccountId);
    expect((values.actualDate as Date).toDateString()).toBe(new Date(2026, 9, 5).toDateString());
    expect(values.createWithdrawal).toBe(true);
    expect(values.rememberAccount).toBe(false);
    expect(field('createWithdrawal')).not.toBeNull();
    expect(submitButton().disabled).toBe(false);
  });

  it('requires an account', async () => {
    await setup({ oneOffExpense: oneOff });

    expect(component['form'].getRawValue().accountId).toBeNull();
    expect(submitButton().disabled).toBe(true);

    await component['onSubmit']();
    expect(puts()).toHaveLength(0);

    component['form'].controls.accountId.setValue(plnAccountId);
    await settle();
    expect(submitButton().disabled).toBe(false);
  });

  it('hides remember for the same account', async () => {
    await setup({ payment });

    expect(field('rememberAccount')).toBeNull();

    component['form'].controls.accountId.setValue(eurAccountId);
    await settle();

    expect(field('rememberAccount')).not.toBeNull();
    const suffixes = Array.from(element().querySelectorAll('.mat-mdc-form-field-text-suffix')).map(
      (suffix) => suffix.textContent?.trim(),
    );
    expect(suffixes).toContain('EUR');
  });

  it('submits to the recurring payment endpoint', async () => {
    await setup({ payment });

    component['form'].controls.actualAmount.setValue(2350);
    component['form'].controls.accountId.setValue(otherPlnAccountId);
    component['form'].controls.rememberAccount.setValue(true);
    await settle();
    await component['onSubmit']();

    const [request] = puts();
    expect(request.url).toContain(
      '/api/portfolio/recurring-expenses/expense-1/payments/2026-10/settlement',
    );
    expect(await request.json()).toEqual({
      actualAmount: 2350,
      actualDate: '2026-10-05',
      accountId: otherPlnAccountId,
      createWithdrawal: true,
      rememberAccount: true,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('submits to the one-off endpoint', async () => {
    await setup({ oneOffExpense: oneOff });

    component['form'].controls.accountId.setValue(eurAccountId);
    component['form'].controls.actualAmount.setValue(25);
    component['form'].controls.createWithdrawal.setValue(false);
    await settle();
    await component['onSubmit']();

    const [request] = puts();
    expect(request.url).toContain('/api/portfolio/one-off-expenses/one-off-expense-1/settlement');
    expect(await request.json()).toEqual({
      actualAmount: 25,
      actualDate: '2026-10-03',
      accountId: eurAccountId,
      createWithdrawal: false,
      rememberAccount: false,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('opens a settled payment filled with its settlement', async () => {
    await setup({
      payment: {
        ...payment,
        status: 'Settled',
        accountId: otherPlnAccountId,
        accountName: 'Rezerwa',
        actualAmount: 2350,
        actualDate: '2026-10-06',
        actualAmountPln: 2350,
        difference: -50,
        withdrawalCreated: false,
      },
    });

    const values = component['form'].getRawValue();
    expect(values.actualAmount).toBe(2350);
    expect(values.accountId).toBe(otherPlnAccountId);
    expect((values.actualDate as Date).toDateString()).toBe(new Date(2026, 9, 6).toDateString());
    expect(values.createWithdrawal).toBe(false);
  });

  it('shows insufficient funds', async () => {
    await setup(
      { payment },
      () =>
        new Response(
          JSON.stringify({
            detail: 'Cash would go below zero.',
            errorCode: 'Validation.InsufficientFunds',
          }),
          { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
        ),
    );

    await component['onSubmit']();
    await settle();

    expect(element().querySelector('[role="alert"]')?.textContent).toContain(
      'Cash would go below zero.',
    );
    expect(dialogRef.close).not.toHaveBeenCalled();
  });
});
