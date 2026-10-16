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

  async function setup(): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      const request = input as Request;
      if (request.method === 'PUT') {
        return new Response(null, { status: 204 });
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
        { provide: MAT_DIALOG_DATA, useValue: { occurrence } },
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

  it('prefills the plan, today and the planned account, lists only non-archived PLN accounts, rejects a future date and puts amount, date and account', async () => {
    await setup();

    const values = component['form'].getRawValue();
    expect(values.actualAmount).toBe(9200);
    expect(values.accountId).toBe(plnAccountId);
    expect((values.actualDate as Date).toDateString()).toBe(new Date(2026, 9, 20).toDateString());

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
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });
});
