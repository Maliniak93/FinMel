import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { jsonResponse } from '../../assets/asset-form/testing/asset-form-fixtures';
import { SettleOccurrenceDialog } from './settle-occurrence-dialog';

const plnAccountId = '11111111-1111-1111-1111-111111111111';
const otherPlnAccountId = '44444444-4444-4444-4444-444444444444';
const eurAccountId = '22222222-2222-2222-2222-222222222222';
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
  };
}

const occurrence = {
  recurringIncomeId: 'income-1',
  nominalMonth: '2026-10',
  date: '2026-10-30',
  name: 'Wypłata Dominik',
  accountId: plnAccountId,
  accountName: 'mBank ROR',
  accountArchived: false,
  amount: 9200,
  currency: 'PLN',
  amountPln: 9200,
  estimated: false,
  status: 'Pending',
  outOfRule: false,
  plannedAccountId: plnAccountId,
  actualAmount: null,
  actualDate: null,
  difference: null,
  actualAmountPln: null,
  depositCreated: false,
};

describe('SettleOccurrenceDialog', () => {
  let fixture: ComponentFixture<SettleOccurrenceDialog>;
  let component: SettleOccurrenceDialog;
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
    data: Record<string, unknown> = occurrence,
    putResponse: () => Response = () => new Response(null, { status: 204 }),
    dataKey: 'occurrence' | 'oneOffIncome' = 'occurrence',
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      if (request.method === 'PUT') {
        return putResponse();
      }
      return jsonResponse([
        account(plnAccountId, 'mBank ROR', 'PLN'),
        account(otherPlnAccountId, 'Konto firmowe', 'PLN'),
        account(eurAccountId, 'Revolut EUR', 'EUR'),
        account(archivedAccountId, 'Stare konto', 'PLN', true),
      ]);
    });

    await TestBed.configureTestingModule({
      imports: [SettleOccurrenceDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: { [dataKey]: data } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SettleOccurrenceDialog);
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

  it('puts the settlement of a one-off income to its own endpoint', async () => {
    await setup(
      {
        id: 'one-off-1',
        name: 'Premia',
        date: '2026-10-05',
        accountId: plnAccountId,
        accountName: 'mBank ROR',
        accountArchived: false,
        amount: 2500,
        currency: 'PLN',
        amountPln: 2500,
        estimated: false,
        inMonth: true,
        status: 'Pending',
        actualAmount: null,
        actualDate: null,
        actualAmountPln: null,
        difference: null,
        depositCreated: false,
        overdue: true,
      },
      undefined,
      'oneOffIncome',
    );

    await component['onSubmit']();

    const [request] = puts();
    expect(request.url).toContain('/api/portfolio/one-off-incomes/one-off-1/settlement');
    expect(await request.json()).toMatchObject({
      actualAmount: 2500,
      accountId: plnAccountId,
      createDeposit: true,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('prefills the plan, today and the planned account, lists only non-archived PLN accounts, rejects a future date and puts amount, date and account', async () => {
    await setup();

    const values = component['form'].getRawValue();
    expect(values.actualAmount).toBe(9200);
    expect(values.accountId).toBe(plnAccountId);
    expect((values.actualDate as Date).toDateString()).toBe(new Date(2026, 9, 20).toDateString());

    const amount = element().querySelector<HTMLElement>('[formControlName="actualAmount"]')!;
    expect(amount.classList).toContain('app-amount');
    const formField = amount.closest('mat-form-field')!;
    expect(formField.classList).toContain('mat-mdc-form-field-label-always-float');
    expect(formField.querySelector('.mat-mdc-form-field-text-suffix')?.textContent?.trim()).toMatch(
      /^(zł|PLN)$/,
    );

    element()
      .querySelector<HTMLElement>('[formControlName="accountId"] .mat-mdc-select-trigger')!
      .click();
    await settle();
    const options = Array.from(
      TestBed.inject(OverlayContainer)
        .getContainerElement()
        .querySelectorAll<HTMLElement>('mat-option'),
    ).map((option) => (option.textContent ?? '').replace(/\s+/g, ' ').trim());
    expect(options).toHaveLength(2);
    expect(options.join(' ')).toContain('mBank ROR');
    expect(options.join(' ')).toContain('Konto firmowe');
    expect(options.join(' ')).not.toContain('Revolut EUR');
    expect(options.join(' ')).not.toContain('Stare konto');

    component['form'].controls.actualDate.setValue(new Date(2026, 9, 21));
    await component['onSubmit']();

    expect(component['form'].controls.actualDate.invalid).toBe(true);
    expect(puts()).toHaveLength(0);

    component['form'].controls.actualAmount.setValue(9143.27);
    component['form'].controls.actualDate.setValue(new Date(2026, 9, 19));
    component['form'].controls.accountId.setValue(otherPlnAccountId);
    await component['onSubmit']();

    const [request] = puts();
    expect(request.url).toContain(
      '/api/portfolio/recurring-incomes/income-1/occurrences/2026-10/settlement',
    );
    expect(await request.json()).toEqual({
      actualAmount: 9143.27,
      actualDate: '2026-10-19',
      accountId: otherPlnAccountId,
      createDeposit: true,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('checks the deposit box for a new settlement and sends the unticked choice', async () => {
    await setup();

    const checkbox = element().querySelector<HTMLInputElement>('mat-checkbox input')!;
    expect(checkbox.checked).toBe(true);
    expect(element().textContent).toContain('Create a deposit on the account');

    checkbox.click();
    await settle();
    await component['onSubmit']();

    const [request] = puts();
    expect(await request.json()).toMatchObject({ createDeposit: false });
  });

  const settledOccurrence = {
    ...occurrence,
    status: 'Settled',
    actualAmount: 9143.27,
    actualDate: '2026-10-19',
  };

  it('editing a settlement without a deposit leaves the box unchecked and sends false', async () => {
    await setup({ ...settledOccurrence, depositCreated: false });

    expect(element().querySelector<HTMLInputElement>('mat-checkbox input')!.checked).toBe(false);
    await component['onSubmit']();
    expect(await puts()[0].json()).toMatchObject({ createDeposit: false });
  });

  it('editing a settlement with a deposit leaves the box checked and sends true', async () => {
    await setup({ ...settledOccurrence, depositCreated: true });

    expect(element().querySelector<HTMLInputElement>('mat-checkbox input')!.checked).toBe(true);
    await component['onSubmit']();
    expect(await puts()[0].json()).toMatchObject({ createDeposit: true });
  });

  it('shows the insufficient-funds message in the dialog and keeps it open', async () => {
    await setup(
      occurrence,
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

  it('shows the archived-account error on the account field', async () => {
    await setup(
      occurrence,
      () =>
        new Response(
          JSON.stringify({ detail: 'Archived.', errorCode: 'Conflict.AccountArchived' }),
          { status: 409, headers: { 'Content-Type': 'application/problem+json' } },
        ),
    );

    await component['onSubmit']();
    await settle();

    expect(component['form'].controls.accountId.getError('server')).toBe(
      'This account is archived.',
    );
    expect(dialogRef.close).not.toHaveBeenCalled();
  });
});
