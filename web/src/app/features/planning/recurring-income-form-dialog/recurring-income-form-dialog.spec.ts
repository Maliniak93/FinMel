import type { IncomeDefinitionResponse } from '../../../api/portfolio';
import { OverlayContainer } from '@angular/cdk/overlay';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { jsonResponse } from '../../assets/asset-form/testing/asset-form-fixtures';
import { RecurringIncomeFormDialog } from './recurring-income-form-dialog';

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
  };
}

describe('RecurringIncomeFormDialog', () => {
  let fixture: ComponentFixture<RecurringIncomeFormDialog>;
  let component: RecurringIncomeFormDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    vi.useRealTimers();
    fetchSpy.mockRestore();
  });

  async function setup(
    data: { definition: IncomeDefinitionResponse } | null = null,
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      if (request.method === 'PUT') {
        return new Response(null, { status: 204 });
      }
      if (request.method !== 'GET') {
        return jsonResponse({ id: 'new-income' }, 201);
      }
      return jsonResponse([
        account(plnAccountId, 'mBank ROR', 'PLN'),
        account(eurAccountId, 'Revolut EUR', 'EUR'),
        account(archivedAccountId, 'Stare konto', 'PLN', true),
      ]);
    });

    await TestBed.configureTestingModule({
      imports: [RecurringIncomeFormDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RecurringIncomeFormDialog);
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

  it('hides the day and shift fields for the last business day and posts no day with shift None', async () => {
    await setup();
    expect(field('dayOfMonth')).not.toBeNull();
    expect(field('shift')).not.toBeNull();

    component['form'].controls.name.setValue('Wypłata Dominik');
    component['form'].controls.amount.setValue(9200);
    component['form'].controls.accountId.setValue(plnAccountId);
    component['form'].controls.startDate.setValue(new Date(2026, 0, 1));
    component['form'].controls.dayKind.setValue(1);
    await settle();

    expect(field('dayOfMonth')).toBeNull();
    expect(field('shift')).toBeNull();

    await component['onSubmit']();

    const [request] = posts();
    expect(request.url).toContain('/api/portfolio/recurring-incomes');
    const body = await request.json();
    expect(body).toMatchObject({
      name: 'Wypłata Dominik',
      amount: 9200,
      accountId: plnAccountId,
      startDate: '2026-01-01',
      intervalMonths: 1,
      dayKind: 1,
      shift: 0,
    });
    expect(body.dayOfMonth ?? null).toBeNull();
    expect(body.endDate ?? null).toBeNull();
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  function definition(extra: Partial<IncomeDefinitionResponse> = {}): IncomeDefinitionResponse {
    return {
      id: 'income-1',
      name: 'Wypłata Dominik',
      startDate: '2026-01-01',
      endDate: null,
      status: 'Active',
      amount: 9200,
      currency: 'PLN',
      accountId: plnAccountId,
      accountName: 'mBank ROR',
      accountArchived: false,
      intervalMonths: 1,
      dayKind: 1,
      dayOfMonth: null,
      shift: 0,
      effectiveFrom: '2026-01',
      nextChange: null,
      ...extra,
    };
  }

  function puts(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((request: Request) => request.method === 'PUT');
  }

  it('prefills the viewed month values in edit mode and puts them with the current month as effectiveFrom', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(2026, 9, 20, 12));
    await setup({ definition: definition() });

    expect(component['form'].getRawValue()).toMatchObject({
      name: 'Wypłata Dominik',
      amount: 9200,
      accountId: plnAccountId,
      effectiveFrom: '2026-10',
      intervalMonths: 1,
      dayKind: 1,
    });
    expect(field('effectiveFrom')).not.toBeNull();
    expect(field('dayOfMonth')).toBeNull();

    component['form'].controls.amount.setValue(9800);
    component['form'].controls.effectiveFrom.setValue('2027-01');
    await component['onSubmit']();

    expect(posts()).toHaveLength(0);
    const [request] = puts();
    expect(request.url).toContain('/api/portfolio/recurring-incomes/income-1');
    expect(await request.json()).toMatchObject({
      name: 'Wypłata Dominik',
      startDate: '2026-01-01',
      effectiveFrom: '2027-01',
      amount: 9800,
      accountId: plnAccountId,
      intervalMonths: 1,
      dayKind: 1,
      shift: 0,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('rejects an effective month outside 2011-01 to 2099-12 and keeps an archived current account selectable', async () => {
    await setup({
      definition: definition({ accountId: archivedAccountId, accountName: 'Stare konto' }),
    });

    expect(component['accounts']().map((account) => account.id)).toContain(archivedAccountId);

    component['form'].controls.effectiveFrom.setValue('2010-12');
    await component['onSubmit']();

    expect(puts()).toHaveLength(0);
    expect(component['form'].controls.effectiveFrom.hasError('monthOutOfRange')).toBe(true);
  });

  it('has no effective month field when adding', async () => {
    await setup();

    expect(field('effectiveFrom')).toBeNull();
  });

  it('lists only the non-archived accounts with their currency', async () => {
    await setup();

    field('accountId')!.querySelector<HTMLElement>('.mat-mdc-select-trigger')!.click();
    await settle();
    const options = Array.from(
      TestBed.inject(OverlayContainer)
        .getContainerElement()
        .querySelectorAll<HTMLElement>('mat-option'),
    ).map((option) => (option.textContent ?? '').replace(/\s+/g, ' ').trim());

    expect(options).toHaveLength(2);
    expect(options[0]).toContain('mBank ROR');
    expect(options[0]).toContain('PLN');
    expect(options[1]).toContain('Revolut EUR');
    expect(options[1]).toContain('EUR');
    expect(options.join(' ')).not.toContain('Stare konto');
  });
});
