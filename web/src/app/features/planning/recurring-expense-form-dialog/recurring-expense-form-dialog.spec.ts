import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { jsonResponse } from '../../assets/asset-form/testing/asset-form-fixtures';
import { RecurringExpenseFormDialog } from './recurring-expense-form-dialog';

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
      if ((input as Request).method === 'PUT') {
        return new Response(null, { status: 204 });
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

  it('has no account or currency field', async () => {
    await setup();

    expect(field('name')).not.toBeNull();
    expect(field('amount')).not.toBeNull();
    expect(field('startDate')).not.toBeNull();
    expect(field('endDate')).not.toBeNull();
    expect(field('accountId')).toBeNull();
    expect(field('currency')).toBeNull();
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
    expect(body.accountId).toBeUndefined();
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
    expect(field('accountId')).toBeNull();
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
    expect(body.accountId).toBeUndefined();
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
});
