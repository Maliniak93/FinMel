import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import {
  labelsOf,
  matchesTranslation,
  polishProblems,
  restoreEnglish,
  switchLanguage,
} from '../../../../testing/i18n';
import { provideI18nTesting } from '../../../core/i18n/testing';
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
  eurSavingsCandidate,
  paidOutDeposit,
  pickSelectOption,
  plnCashCandidate,
  plnSavingsCandidate,
  requestedCandidateClasses,
  requestMethod,
  savingsCandidatesByCurrency,
  selectOptionLabels,
  settledDeposit,
  settledDepositFinalAmount,
  transferCandidateRequests,
  transferCandidatesByCurrency,
  transferCandidatesFor,
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

  afterEach(async () => {
    fetchSpy.mockRestore();
    // Specs share one worker (isolate: false) — never leave Polish active for the next file.
    await restoreEnglish();
  });

  // The transfer candidates answer per the `currency` query parameter from
  // `transferCandidatesByCurrency`; every write answers with `writeResponse`.
  async function setup(
    writeResponse: () => Response = () => jsonResponse(paidOutDeposit),
    candidatesByCurrency: typeof transferCandidatesByCurrency = transferCandidatesByCurrency,
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (requestMethod(input) === 'GET') {
        if (requestUrl(input).includes('/api/portfolio/transfer-candidates')) {
          // `candidatesByCurrency` arranges the Cash candidates; a spec that overrides it has no
          // savings accounts either.
          return jsonResponse(
            transferCandidatesFor(
              new URL(requestUrl(input)),
              candidatesByCurrency,
              candidatesByCurrency === transferCandidatesByCurrency
                ? savingsCandidatesByCurrency
                : {},
            ),
          );
        }
        return jsonResponse({ detail: 'Not found.' }, 404);
      }
      return writeResponse();
    });

    await TestBed.configureTestingModule({
      imports: [PayOutDepositDialog],
      providers: [
        provideI18nTesting(),
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

  it('lists the Cash and savings candidates in the deposit currency, with no destination preselected', async () => {
    await setup();

    expect(findControl(form(), 'destinationAssetId').value ?? null).toBeNull();
    const requested = requestedCandidateClasses(fetchSpy);
    expect(requested).toContain('Cash');
    expect(requested).toContain('Savings');
    for (const url of transferCandidateRequests(fetchSpy)) {
      expect(url.searchParams.get('currency')).toBe(settledDeposit.currency);
    }

    const labels = await selectOptionLabels(fixture, 'destinationAssetId');
    expect(labels.some((label) => label.includes(plnCashCandidate.name))).toBe(true);
    expect(labels.some((label) => label.includes(eurCashCandidate.name))).toBe(false);
    expect(labels.some((label) => label.includes(plnSavingsCandidate.name))).toBe(true);
    expect(labels.some((label) => label.includes(eurSavingsCandidate.name))).toBe(false);
    // A payout can't keep the money in the deposit.
    expect(labels.some((label) => /keep in the deposit/i.test(label))).toBe(false);
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

  // deposit-payout-to-savings AC-10: a savings account is a destination like a Cash asset.
  it('submit POSTs a savings account as destinationAssetId', async () => {
    await setup();
    await pickSelectOption(fixture, 'destinationAssetId', plnSavingsCandidate.name);
    await fill({ date: new Date(2026, 3, 18) });

    await component['onSubmit']();

    const [request] = writeRequests(fetchSpy);
    expect(request.method).toBe('POST');
    expect(request.url).toContain(
      `/api/portfolio/portfolios/${settledDeposit.portfolioId}/deposits/${settledDeposit.assetId}/payout`,
    );
    expect(await request.json()).toEqual({
      destinationAssetId: plnSavingsCandidate.assetId,
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

  // i18n screens (#132) AC-6: the dialog's title, summary, field labels, hints, validation messages
  // and buttons follow the language.
  describe('in Polish', () => {
    function errors(): string[] {
      return labelsOf(fixture.nativeElement as HTMLElement, 'mat-error');
    }

    async function touchAll(): Promise<void> {
      await component['onSubmit']();
      await render();
    }

    it('renders in Polish', async () => {
      await setup();
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...labelsOf(element, 'h2'),
        ...labelsOf(element, 'dt'),
        ...labelsOf(element, 'mat-label'),
        ...labelsOf(element, 'mat-dialog-actions button'),
      ];

      const english = texts();
      expect(english).toEqual([
        'Pay out',
        'Amount',
        'Move to',
        'Date',
        'Cancel',
        'Transfer',
      ]);

      await switchLanguage(fixture, 'pl');

      // "Transfer" may stay as it is in Polish.
      expect(polishProblems(english, texts(), ['Transfer'])).toEqual([]);
    });

    it('shows validation in Polish', async () => {
      await setup();
      const scenarios: [string, Record<string, unknown>][] = [
        ['Pick where the money goes.', { destinationAssetId: null, date: daysFromToday(0) }],
        [
          "Date can't be before the settlement date.",
          { destinationAssetId: plnCashCandidate.assetId, date: new Date(2026, 3, 16) },
        ],
        [
          "Date can't be in the future.",
          { destinationAssetId: plnCashCandidate.assetId, date: daysFromToday(1) },
        ],
      ];
      const read = async (values: Record<string, unknown>) => {
        await fill(values);
        await touchAll();
        return errors();
      };

      const english: string[][] = [];
      for (const [message, values] of scenarios) {
        english.push(await read(values));
        expect(english.at(-1)).toEqual([message]);
      }

      await switchLanguage(fixture, 'pl');

      for (const [index, [, values]] of scenarios.entries()) {
        expect(polishProblems(english[index], await read(values))).toEqual([]);
      }
    });

    // deposit-payout-to-savings: the hint shows when there is neither a Cash nor a savings account.
    it('shows the missing-account hint in Polish', async () => {
      await setup(undefined, { PLN: [] });
      const element = fixture.nativeElement as HTMLElement;
      const hint = () => labelsOf(element, 'mat-hint');

      const english = hint();
      expect(english).toEqual(['You have no Cash or savings account in PLN to move the money to.']);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, hint())).toEqual([]);
      expect(hint()[0]).toContain('PLN');
      expect(matchesTranslation('pl', hint()[0]), `"${hint()[0]}" is not a pl.json value`).toBe(
        true,
      );
    });
  });
});
