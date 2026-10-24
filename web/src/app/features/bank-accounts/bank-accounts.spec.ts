import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { restoreEnglish, switchLanguage } from '../../../testing/i18n';
import { BANK_VARIANT, bankAccountResponse } from '../../../testing/bank-account-fixtures';
import { provideI18nTesting } from '../../core/i18n/testing';
import { client as portfolioClient } from '../../api/portfolio/client.gen';
import { formatMoney } from '../../shared/format';
import { BankAccounts } from './bank-accounts';

function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

const mBank = bankAccountResponse({
  id: '11111111-1111-1111-1111-111111111111',
  name: 'mBank',
  cashBalance: 5000,
  bank: { variant: BANK_VARIANT.Current, bankName: 'mBank', annualInterestRatePercent: 0 },
});

const nest = bankAccountResponse({
  id: '22222222-2222-2222-2222-222222222222',
  name: 'Nest Oszczędnościowe',
  cashBalance: 10000,
  bank: {
    variant: BANK_VARIANT.Savings,
    bankName: 'Nest Bank',
    annualInterestRatePercent: 5,
    interestDue: true,
    duePeriodCount: 1,
    interestCreditedNet: 34.4,
    lastSettlement: {
      settlementId: '99999999-9999-9999-9999-999999999999',
      periodStart: '2026-07-01',
      periodEnd: '2026-07-31',
      grossInterest: 42.47,
      tax: 8.07,
      netInterest: 34.4,
    },
  },
});

const archived = bankAccountResponse({
  id: '33333333-3333-3333-3333-333333333333',
  name: 'Old bank',
  isArchived: true,
  cashBalance: 0,
});

describe('BankAccounts', () => {
  let fixture: ComponentFixture<BankAccounts>;
  let component: BankAccounts;
  let fetchSpy: ReturnType<typeof vi.spyOn>;
  let dialog: { open: ReturnType<typeof vi.fn> };
  let snackBar: { open: ReturnType<typeof vi.fn> };

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(accounts = [mBank, nest]): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async () => jsonResponse(accounts));
    dialog = { open: vi.fn() };
    snackBar = { open: vi.fn() };

    await TestBed.configureTestingModule({
      imports: [BankAccounts],
      providers: [
        provideRouter([]),
        provideI18nTesting(),
        { provide: MatDialog, useValue: dialog },
        { provide: MatSnackBar, useValue: snackBar },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BankAccounts);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  function pageText(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function listReloadedFrom(index: number): boolean {
    return fetchSpy.mock.calls
      .slice(index)
      .map(([input]: [unknown]) => input as Request)
      .some(
        (request: Request) =>
          request.method === 'GET' && request.url.includes('/api/portfolio/accounts'),
      );
  }

  async function pickVariant(label: string): Promise<void> {
    const chip = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>(
        'mat-chip-option button[role="option"]',
      ),
    ).find((option) => (option.textContent ?? '').trim() === label);
    expect(chip, `no variant chip "${label}"`).toBeDefined();
    chip!.click();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  it('lists only Bank accounts with their terms, balance and credited interest', async () => {
    await setup();

    const request = fetchSpy.mock.calls[0][0] as Request;
    expect(new URL(request.url).searchParams.get('type')).toBe('2');
    expect(pageText()).toContain('mBank');
    expect(pageText()).toContain('Nest Oszczędnościowe');
    expect(pageText()).toContain('Nest Bank');
    expect(pageText()).toContain('PLN');
    expect(pageText()).toContain(formatMoney(10000, 'PLN'));
    expect(pageText()).toContain(formatMoney(34.4, 'PLN'));
  });

  it('shows the "Odsetki do rozliczenia" chip only on an account with interest due', async () => {
    await setup();
    await switchLanguage(fixture, 'pl');

    const rows = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLElement>('tr'),
    );
    const nestRow = rows.find((row) => (row.textContent ?? '').includes('Nest Oszczędnościowe'));
    const mBankRow = rows.find((row) => (row.textContent ?? '').includes('mBank'));

    expect(nestRow?.textContent).toContain('Odsetki do rozliczenia');
    expect(mBankRow?.textContent).not.toContain('Odsetki do rozliczenia');
  });

  it('filters by variant: Bieżące shows mBank only, Oszczędnościowe Nest only, Wszystkie both', async () => {
    await setup();
    await switchLanguage(fixture, 'pl');

    await pickVariant('Bieżące');
    expect(pageText()).toContain('mBank');
    expect(pageText()).not.toContain('Nest Oszczędnościowe');

    await pickVariant('Oszczędnościowe');
    expect(pageText()).not.toContain('mBank');
    expect(pageText()).toContain('Nest Oszczędnościowe');

    await pickVariant('Wszystkie');
    expect(pageText()).toContain('mBank');
    expect(pageText()).toContain('Nest Oszczędnościowe');
  });

  it('shows archived rows only with the toggle on', async () => {
    await setup([mBank, archived]);

    expect(pageText()).not.toContain('Old bank');

    component['includeArchived'].set(true);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(pageText()).toContain('Old bank');
  });

  it('links each row to its account page', async () => {
    await setup();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector(`a[href="/wealth/bank/${mBank.id}"]`)).not.toBeNull();
    expect(element.querySelector(`a[href="/wealth/bank/${nest.id}"]`)).not.toBeNull();
  });

  it('opens the create dialog preset to Bank and reloads after a save', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;

    component['openCreateDialog']();
    await fixture.whenStable();

    expect(dialog.open.mock.calls[0][1].data).toMatchObject({ type: 2 });
    expect(listReloadedFrom(callsBefore)).toBe(true);
  });

  it('opens the edit dialog with the account and reloads after a save', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;

    component['openEditDialog'](nest);
    await fixture.whenStable();

    expect(dialog.open.mock.calls[0][1].data).toMatchObject({ account: nest });
    expect(listReloadedFrom(callsBefore)).toBe(true);
  });

  it('opens the settle-interest dialog with the account and reloads after a save', async () => {
    await setup();
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const callsBefore = fetchSpy.mock.calls.length;

    component['openSettleDialog'](nest);
    await fixture.whenStable();

    expect(dialog.open.mock.calls[0][1].data).toMatchObject({ account: nest });
    expect(listReloadedFrom(callsBefore)).toBe(true);
  });

  it('undoes the last settlement after confirmation and reloads', async () => {
    await setup();
    const callsBefore = fetchSpy.mock.calls.length;
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    fetchSpy.mockResolvedValueOnce(new Response(null, { status: 204 }));

    await component['undoLastSettlement'](nest);
    await fixture.whenStable();

    const call = fetchSpy.mock.calls[callsBefore][0] as Request;
    expect(call.method).toBe('DELETE');
    expect(call.url).toContain(
      `/api/portfolio/accounts/${nest.id}/interest-settlements/${nest.bank!.lastSettlement!.settlementId}`,
    );
    expect(listReloadedFrom(callsBefore + 1)).toBe(true);
  });

  it('explains an archive or delete refused because an open deposit returns to the account', async () => {
    await setup();
    await switchLanguage(fixture, 'pl');
    dialog.open.mockReturnValue({ afterClosed: () => of(true) });
    const refusal = () =>
      jsonResponse(
        {
          status: 409,
          detail: 'A deposit will still return to this account.',
          errorCode: 'Conflict.AccountHasOpenDeposit',
        },
        409,
      );

    fetchSpy.mockResolvedValueOnce(refusal());
    await component['archive'](nest);
    fetchSpy.mockResolvedValueOnce(refusal());
    await component['remove'](nest);

    expect(snackBar.open).toHaveBeenCalledTimes(2);
    for (const [message] of snackBar.open.mock.calls) {
      expect(message).toBe('Na to konto wróci otwarta lokata');
    }
  });
});
