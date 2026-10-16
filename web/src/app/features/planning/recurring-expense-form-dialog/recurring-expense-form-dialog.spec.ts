import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MatDialogRef } from '@angular/material/dialog';

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
    fetchSpy.mockRestore();
  });

  async function setup(): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async () => jsonResponse({ id: 'new-expense' }, 201));

    await TestBed.configureTestingModule({
      imports: [RecurringExpenseFormDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
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
});
