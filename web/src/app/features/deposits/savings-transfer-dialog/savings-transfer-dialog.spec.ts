import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { restoreEnglish } from '../../../../testing/i18n';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { toDateOnly } from '../../../shared/date-only';
import { formatMoney } from '../../../shared/format';
import {
  findControl,
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  eurCashCandidate,
  plnCashCandidate,
  requestMethod,
  selectOptionLabels,
  transferCandidateRequests,
  transferCandidatesByCurrency,
  writeRequests,
} from '../testing/deposit-fixtures';
import { savingsAccountResponse } from '../testing/savings-account-fixtures';
import { SavingsTransferDialog } from './savings-transfer-dialog';

describe('SavingsTransferDialog', () => {
  const account = savingsAccountResponse({ balance: 10000 });
  let fixture: ComponentFixture<SavingsTransferDialog>;
  let component: SavingsTransferDialog;
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
    writeResponse: () => Response = () =>
      jsonResponse({ transferId: 'cccccccc-cccc-cccc-cccc-cccccccccccc' }, 201),
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (requestMethod(input) === 'GET') {
        if (requestUrl(input).includes('/api/portfolio/transfer-candidates')) {
          const currency = new URL(requestUrl(input)).searchParams.get('currency') ?? '';
          return jsonResponse(transferCandidatesByCurrency[currency] ?? []);
        }
        return jsonResponse({ detail: 'Not found.' }, 404);
      }
      return writeResponse();
    });

    await TestBed.configureTestingModule({
      imports: [SavingsTransferDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: { account } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SavingsTransferDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function form(): FormGroup {
    return component['form'] as FormGroup;
  }

  async function fill(values: Record<string, unknown>): Promise<void> {
    for (const [name, value] of Object.entries(values)) {
      const control = findControl(form(), name);
      control.setValue(value);
      control.markAsDirty();
    }
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function render(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
  }

  function daysFromToday(days: number): Date {
    const date = new Date();
    date.setHours(0, 0, 0, 0);
    date.setDate(date.getDate() + days);
    return date;
  }

  it('defaults the direction to "Into this account" and the date to today', async () => {
    await setup();

    expect(findControl(form(), 'direction').value).toBe('in');
    expect(toDateOnly(findControl(form(), 'date').value as Date)).toBe(
      toDateOnly(daysFromToday(0)),
    );
    expect(renderedText(fixture)).toContain('Into this account');
  });

  it('lists the Cash candidates in the account currency with their balances', async () => {
    await setup();

    expect(findControl(form(), 'cashAssetId').value ?? null).toBeNull();
    const requests = transferCandidateRequests(fetchSpy);
    expect(requests.length).toBeGreaterThan(0);
    const last = requests[requests.length - 1];
    expect(last.searchParams.get('currency')).toBe(account.currency);
    expect(['Cash', '0']).toContain(last.searchParams.get('assetClass'));

    const labels = await selectOptionLabels(fixture, 'cashAssetId');
    const label = labels.find((text) => text.includes(plnCashCandidate.name));
    expect(label).toBeDefined();
    expect(label).toContain(plnCashCandidate.portfolioName);
    expect(label!.replace(/\s+/g, ' ')).toContain(
      formatMoney(plnCashCandidate.balance, 'PLN').replace(/\s+/g, ' '),
    );
    expect(labels.some((text) => text.includes(eurCashCandidate.name))).toBe(false);
  });

  it.each([
    ['no Cash selected', { cashAssetId: null, amount: 100 }],
    ['an amount of 0', { cashAssetId: plnCashCandidate.assetId, amount: 0 }],
    ['an empty amount', { cashAssetId: plnCashCandidate.assetId, amount: null }],
    [
      'a date in the future',
      { cashAssetId: plnCashCandidate.assetId, amount: 100, date: daysFromToday(1) },
    ],
  ])('%s blocks submit', async (_case, values) => {
    await setup();

    await fill(values);
    await component['onSubmit']();

    expect(form().invalid).toBe(true);
    expect(writeRequests(fetchSpy)).toEqual([]);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('an amount above the selected Cash balance blocks submit going in', async () => {
    await setup();

    await fill({ direction: 'in', cashAssetId: plnCashCandidate.assetId, amount: 5000.01 });
    await component['onSubmit']();

    expect(findControl(form(), 'amount').invalid).toBe(true);
    expect(writeRequests(fetchSpy)).toEqual([]);

    await fill({ amount: 5000 });
    expect(findControl(form(), 'amount').valid).toBe(true);
  });

  it('an amount above the account balance blocks submit going out, and the Cash balance no longer limits it', async () => {
    await setup();

    await fill({ direction: 'out', cashAssetId: plnCashCandidate.assetId, amount: 10000.01 });
    await component['onSubmit']();

    expect(findControl(form(), 'amount').invalid).toBe(true);
    expect(writeRequests(fetchSpy)).toEqual([]);

    await fill({ amount: 8000 });
    expect(findControl(form(), 'amount').valid).toBe(true);
  });

  it('going in, submit POSTs Cash as the source and the account as the target and closes with true', async () => {
    await setup();
    await fill({
      direction: 'in',
      cashAssetId: plnCashCandidate.assetId,
      amount: 2000,
      date: new Date(2026, 0, 20),
    });

    await component['onSubmit']();

    const [request] = writeRequests(fetchSpy);
    expect(request.method).toBe('POST');
    expect(request.url).toContain('/api/portfolio/transfers');
    expect(await request.json()).toEqual({
      sourceAssetId: plnCashCandidate.assetId,
      targetAssetId: account.assetId,
      amount: 2000,
      date: '2026-01-20',
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('going out, submit POSTs the account as the source and Cash as the target', async () => {
    await setup();
    await fill({
      direction: 'out',
      cashAssetId: plnCashCandidate.assetId,
      amount: 500,
      date: new Date(2026, 0, 25),
    });

    await component['onSubmit']();

    const [request] = writeRequests(fetchSpy);
    expect(request.method).toBe('POST');
    expect(await request.json()).toEqual({
      sourceAssetId: account.assetId,
      targetAssetId: plnCashCandidate.assetId,
      amount: 500,
      date: '2026-01-25',
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('a server 400 InsufficientFunds shows on the amount field and the dialog stays open', async () => {
    await setup(() =>
      jsonResponse(
        {
          title: 'Validation Failed',
          status: 400,
          detail: 'The source balance would drop below zero.',
          errorCode: 'Validation.InsufficientFunds',
        },
        400,
      ),
    );
    await fill({ cashAssetId: plnCashCandidate.assetId, amount: 100 });

    await component['onSubmit']();
    await render();

    expect(findControl(form(), 'amount').hasError('server')).toBe(true);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with false on cancel', async () => {
    await setup();

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
