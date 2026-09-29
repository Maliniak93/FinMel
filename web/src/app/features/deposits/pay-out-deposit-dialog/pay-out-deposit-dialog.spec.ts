import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { toDateOnly } from '../../../shared/date-only';
import { formatMoney } from '../../../shared/format';
import {
  findControl,
  hasControl,
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  eurCashCandidate,
  paidOutDeposit,
  plnCashCandidate,
  requestMethod,
  selectOptionLabels,
  settledDeposit,
  settledDepositFinalAmount,
  transferCandidateRequests,
  transferCandidatesByCurrency,
  writeRequests,
} from '../testing/deposit-fixtures';
import { PayOutDepositDialog } from './pay-out-deposit-dialog';

// deposit-payout-to-cash AC-8. The payout dialog opens on a Settled deposit and moves its whole
// balance (principal + settled net interest — 10 121.50 for `settledDeposit`) to a Cash asset: the
// amount is read-only, the destination (control `destinationAssetId`) is required and lists the Cash
// transfer candidates in the deposit's currency, and the date (control `date`) defaults to today and
// must lie between the settlement date and today. Control names follow the PayOutDepositRequest
// properties so a server 400 keyed on a field lands on it. The submit under test is called on the
// dialog itself and asserted on the raw request the generated client hands to `fetch`.
describe('PayOutDepositDialog', () => {
  let fixture: ComponentFixture<PayOutDepositDialog>;
  let component: PayOutDepositDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  // The transfer candidates answer per the `currency` query parameter from
  // `transferCandidatesByCurrency`; every write answers with `writeResponse`.
  async function setup(
    writeResponse: () => Response = () => jsonResponse(paidOutDeposit),
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
      imports: [PayOutDepositDialog],
      providers: [
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: { deposit: settledDeposit } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PayOutDepositDialog);
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

  it('shows the whole balance as a read-only amount', async () => {
    await setup();

    // Either plain text or a disabled control — never something the user could type a part into.
    if (hasControl(form(), 'amount')) {
      const amount = findControl(form(), 'amount');
      expect(amount.disabled).toBe(true);
      expect(Number(amount.value)).toBe(settledDepositFinalAmount);
    } else {
      expect(renderedText(fixture)).toContain(
        formatMoney(settledDepositFinalAmount, settledDeposit.currency),
      );
    }
  });

  it('lists the Cash candidates in the deposit currency, with no destination preselected', async () => {
    await setup();

    expect(findControl(form(), 'destinationAssetId').value ?? null).toBeNull();
    const requests = transferCandidateRequests(fetchSpy);
    expect(requests.length).toBeGreaterThan(0);
    const last = requests[requests.length - 1];
    expect(last.searchParams.get('currency')).toBe(settledDeposit.currency);
    expect(['Cash', '0']).toContain(last.searchParams.get('assetClass'));

    const labels = await selectOptionLabels(fixture, 'destinationAssetId');
    expect(labels.some((label) => label.includes(plnCashCandidate.name))).toBe(true);
    expect(labels.some((label) => label.includes(eurCashCandidate.name))).toBe(false);
  });

  it('defaults the date to today', async () => {
    await setup();

    const date = findControl(form(), 'date').value as Date;
    expect(toDateOnly(date)).toBe(toDateOnly(daysFromToday(0)));
  });

  it.each([
    ['no destination', { destinationAssetId: null }],
    [
      'a date before the settlement date',
      { destinationAssetId: plnCashCandidate.assetId, date: new Date(2026, 3, 16) },
    ],
    [
      'a date in the future',
      { destinationAssetId: plnCashCandidate.assetId, date: daysFromToday(1) },
    ],
  ])('%s blocks submit', async (_case, values) => {
    await setup();

    await fill(values);
    await component['onSubmit']();

    expect(form().invalid).toBe(true);
    expect(writeRequests(fetchSpy)).toEqual([]);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('the settlement date itself is a valid payout date', async () => {
    await setup();

    await fill({ destinationAssetId: plnCashCandidate.assetId, date: new Date(2026, 3, 17) });

    expect(form().valid).toBe(true);
  });

  it('submit POSTs {destinationAssetId, date} and closes with true', async () => {
    await setup();
    await fill({ destinationAssetId: plnCashCandidate.assetId, date: new Date(2026, 3, 18) });

    await component['onSubmit']();

    const [request] = writeRequests(fetchSpy);
    expect(request.method).toBe('POST');
    expect(request.url).toContain(
      `/api/portfolio/portfolios/${settledDeposit.portfolioId}/deposits/${settledDeposit.assetId}/payout`,
    );
    expect(await request.json()).toEqual({
      destinationAssetId: plnCashCandidate.assetId,
      date: '2026-04-18',
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('a server 400 is shown and the dialog stays open', async () => {
    const detail = "This asset can't be the other side of the transfer — pick one from the list.";
    await setup(() =>
      jsonResponse(
        { status: 400, detail, errorCode: 'Validation.InvalidTransferCounterpart' },
        400,
      ),
    );
    await fill({ destinationAssetId: plnCashCandidate.assetId });

    await component['onSubmit']();
    await render();

    expect(renderedText(fixture)).toContain(detail);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('a server 409 is shown and the dialog stays open', async () => {
    const detail = 'This deposit is already paid out.';
    await setup(() =>
      jsonResponse({ status: 409, detail, errorCode: 'Conflict.DepositAlreadyPaidOut' }, 409),
    );
    await fill({ destinationAssetId: plnCashCandidate.assetId });

    await component['onSubmit']();
    await render();

    const banner = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(banner?.textContent).toContain(detail);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with false on cancel', async () => {
    await setup();

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
