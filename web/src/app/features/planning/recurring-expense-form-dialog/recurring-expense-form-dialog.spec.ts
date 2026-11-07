import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { jsonResponse } from '../../assets/asset-form/testing/asset-form-fixtures';
import { RecurringExpenseFormDialog } from './recurring-expense-form-dialog';

const plnAccountId = '11111111-1111-1111-1111-111111111111';
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
    outsideNetWorth: false,
  };
}

const accounts = [
  account(plnAccountId, 'mBank ROR', 'PLN'),
  account(eurAccountId, 'Revolut EUR', 'EUR'),
  account(archivedAccountId, 'Stare konto', 'PLN', true),
];

describe('RecurringExpenseFormDialog', () => {
  let fixture: ComponentFixture<RecurringExpenseFormDialog>;
  let component: RecurringExpenseFormDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    vi.useRealTimers();
    fetchSpy.mockRestore();
  });

  async function setup(data: { definition: Record<string, unknown> } | null = null): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      if (request.method === 'PUT') {
        return new Response(null, { status: 204 });
      }
      if (request.method === 'GET') {
        return jsonResponse(accounts);
      }
      return jsonResponse({ id: 'new-expense' }, 201);
    });

    await TestBed.configureTestingModule({
      imports: [RecurringExpenseFormDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RecurringExpenseFormDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function field(name: string): HTMLElement | null {
    return element().querySelector<HTMLElement>(`[formControlName="${name}"]`);
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function posts(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((request: Request) => request.method === 'POST');
  }

  function puts(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((request: Request) => request.method === 'PUT');
  }

  function definition(extra: Record<string, unknown> = {}): Record<string, unknown> {
    return {
      id: 'expense-1',
      name: 'OC Toyota',
      startDate: '2027-03-15',
      endDate: null,
      status: 'Upcoming',
      amount: 1200,
      intervalMonths: 12,
      dayKind: 0,
      dayOfMonth: 15,
      shift: 0,
      reservePln: 200,
      nextPayment: null,
      effectiveFrom: '2026-01',
      nextChange: null,
      ...extra,
    };
  }

  it('has an optional account field and no currency field', async () => {
    await setup();

    expect(field('name')).not.toBeNull();
    expect(field('amount')).not.toBeNull();
    expect(field('startDate')).not.toBeNull();
    expect(field('endDate')).not.toBeNull();
    expect(field('accountId')).not.toBeNull();
    expect(field('currency')).toBeNull();
    expect(component['form'].controls.accountId.hasError('required')).toBe(false);
  });

  it('shows the złoty suffix on the amount', async () => {
    await setup();

    const amount = field('amount')!;
    expect(amount.classList).toContain('app-amount');
    const formField = amount.closest('mat-form-field')!;
    expect(formField.classList).toContain('mat-mdc-form-field-label-always-float');
    expect(formField.querySelector('.mat-mdc-form-field-text-suffix')?.textContent?.trim()).toMatch(
      /^(zł|PLN)$/,
    );
    expect(formField.querySelector('mat-label')?.textContent).not.toContain('PLN');
  });

  it('hides the day and shift fields for the last business day and posts no day with shift None', async () => {
    await setup();
    expect(field('dayOfMonth')).not.toBeNull();
    expect(field('shift')).not.toBeNull();

    component['form'].controls.name.setValue('OC Toyota');
    component['form'].controls.amount.setValue(1200);
    component['form'].controls.startDate.setValue(new Date(2027, 2, 15));
    component['form'].controls.intervalMonths.setValue(12);
    component['form'].controls.dayKind.setValue(1);
    await settle();

    expect(field('dayOfMonth')).toBeNull();
    expect(field('shift')).toBeNull();

    await component['onSubmit']();

    const [request] = posts();
    expect(request.url).toContain('/api/portfolio/recurring-expenses');
    const body = await request.json();
    expect(body).toMatchObject({
      name: 'OC Toyota',
      amount: 1200,
      startDate: '2027-03-15',
      intervalMonths: 12,
      dayKind: 1,
      shift: 0,
    });
    expect(body.dayOfMonth ?? null).toBeNull();
    expect(body.endDate ?? null).toBeNull();
    expect(body.accountId ?? null).toBeNull();
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('prefills the viewed month values in edit mode and puts them with the current month as effectiveFrom', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 20, 12));
    await setup({ definition: definition() });

    expect(component['form'].getRawValue()).toMatchObject({
      name: 'OC Toyota',
      amount: 1200,
      intervalMonths: 12,
      dayKind: 0,
      effectiveFrom: '2026-10',
    });
    expect(field('effectiveFrom')).not.toBeNull();

    await component['onSubmit']();

    expect(posts()).toHaveLength(0);
    const [request] = puts();
    expect(request.url).toContain('/api/portfolio/recurring-expenses/expense-1');
    expect(await request.json()).toMatchObject({
      name: 'OC Toyota',
      startDate: '2027-03-15',
      effectiveFrom: '2026-10',
      amount: 1200,
      intervalMonths: 12,
      dayKind: 0,
      dayOfMonth: 15,
      shift: 0,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  function modeToggles(): HTMLElement[] {
    return Array.from(element().querySelectorAll<HTMLElement>('mat-button-toggle button'));
  }

  async function chooseOneOff(): Promise<void> {
    modeToggles()
      .find((button) => /jednorazowy|one-off/i.test(button.textContent ?? ''))!
      .click();
    await settle();
  }

  it('posts a one-off expense', async () => {
    await setup();
    expect(modeToggles()).toHaveLength(2);

    await chooseOneOff();

    expect(field('name')).not.toBeNull();
    expect(field('amount')).not.toBeNull();
    expect(field('date')).not.toBeNull();
    expect(field('accountId')).not.toBeNull();
    expect(field('startDate')).toBeNull();
    expect(field('intervalMonths')).toBeNull();
    expect(field('dayOfMonth')).toBeNull();

    component['form'].controls.name.setValue('Przegląd auta');
    component['form'].controls.amount.setValue(1200);
    component['form'].controls.date.setValue(new Date(2026, 10, 10));
    await settle();
    await component['onSubmit']();

    const [request] = posts();
    expect(request.url).toContain('/api/portfolio/one-off-expenses');
    const body = await request.json();
    expect(body).toMatchObject({ name: 'Przegląd auta', amount: 1200, date: '2026-11-10' });
    expect(body.accountId ?? null).toBeNull();
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('edits a one-off expense', async () => {
    await setup({
      oneOff: {
        id: 'one-off-expense-1',
        name: 'Przegląd auta',
        date: '2026-11-10',
        amount: 1200,
        inMonth: false,
      },
    } as never);

    expect(modeToggles()).toHaveLength(0);
    expect(field('date')).not.toBeNull();
    expect(field('startDate')).toBeNull();
    expect(component['form'].getRawValue()).toMatchObject({ name: 'Przegląd auta', amount: 1200 });
    expect((component['form'].getRawValue().date as Date).toDateString()).toBe(
      new Date(2026, 10, 10).toDateString(),
    );

    component['form'].controls.amount.setValue(1350);
    await component['onSubmit']();

    expect(posts()).toHaveLength(0);
    const [request] = puts();
    expect(request.url).toContain('/api/portfolio/one-off-expenses/one-off-expense-1');
    expect(await request.json()).toMatchObject({
      name: 'Przegląd auta',
      amount: 1350,
      date: '2026-11-10',
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('sends the chosen account on a recurring expense', async () => {
    await setup();

    component['form'].controls.name.setValue('Czynsz');
    component['form'].controls.amount.setValue(2400);
    component['form'].controls.startDate.setValue(new Date(2026, 9, 5));
    component['form'].controls.accountId.setValue(plnAccountId);
    await settle();
    await component['onSubmit']();

    const [request] = posts();
    expect(request.url).toContain('/api/portfolio/recurring-expenses');
    expect(await request.json()).toMatchObject({ name: 'Czynsz', accountId: plnAccountId });
  });

  it('sends the chosen account on a one-off expense', async () => {
    await setup();
    await chooseOneOff();

    component['form'].controls.name.setValue('Abonament');
    component['form'].controls.amount.setValue(100);
    component['form'].controls.date.setValue(new Date(2026, 10, 3));
    component['form'].controls.accountId.setValue(eurAccountId);
    await settle();
    await component['onSubmit']();

    const [request] = posts();
    expect(request.url).toContain('/api/portfolio/one-off-expenses');
    expect(await request.json()).toMatchObject({ name: 'Abonament', accountId: eurAccountId });
  });

  it('prefills the account of a definition and of a one-off and sends null once it is cleared', async () => {
    await setup({ definition: definition({ accountId: plnAccountId, accountName: 'mBank ROR' }) });

    expect(component['form'].getRawValue()).toMatchObject({ accountId: plnAccountId });

    component['form'].controls.accountId.setValue(null);
    await component['onSubmit']();
    const [request] = puts();
    expect((await request.json()).accountId ?? null).toBeNull();

    TestBed.resetTestingModule();
    fetchSpy.mockRestore();
    await setup({
      oneOff: {
        id: 'one-off-expense-1',
        name: 'Abonament',
        date: '2026-11-03',
        amount: 100,
        inMonth: true,
        accountId: eurAccountId,
        accountName: 'Revolut EUR',
      },
    } as never);

    expect(component['form'].getRawValue()).toMatchObject({ accountId: eurAccountId });
  });

  it('hides archived accounts', async () => {
    await setup();

    field('accountId')!.querySelector<HTMLElement>('.mat-mdc-select-trigger')!.click();
    await settle();
    const options = Array.from(
      TestBed.inject(OverlayContainer)
        .getContainerElement()
        .querySelectorAll<HTMLElement>('mat-option'),
    ).map((option) => (option.textContent ?? '').replace(/\s+/g, ' ').trim());

    expect(options.join(' ')).toContain('mBank ROR');
    expect(options.join(' ')).toContain('Revolut EUR');
    expect(options.join(' ')).not.toContain('Stare konto');
  });
});
