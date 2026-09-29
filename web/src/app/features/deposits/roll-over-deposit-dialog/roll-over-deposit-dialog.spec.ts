import { formatDate } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import type { DepositResponse } from '../../../api/portfolio';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { formatMoney } from '../../../shared/format';
import {
  findControl,
  hasControl,
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  depositResponse,
  DEPOSIT_STATUS,
  dueDeposit,
  dueDepositSettlementPreview,
  requestMethod,
  rolledOverDeposit,
  settledDeposit,
  writeRequests,
} from '../testing/deposit-fixtures';
import { RollOverDepositDialog } from './roll-over-deposit-dialog';
import { labelsOf, polishProblems, restoreEnglish, switchLanguage } from '../../../../testing/i18n';
import { provideI18nTesting } from '../../../core/i18n/testing';

// deposit-rollover AC-8. The "Roll over" dialog opens on a Due or a Settled (not paid-out) deposit
// and starts its next term on the same asset: the term, capitalisation and the new start date (= the
// old maturity date) are shown read-only, and only the rate (control `annualInterestRatePercent`) is
// editable, pre-filled with the current one. In Due mode it also settles: gross interest and tax
// (controls `grossInterest` / `tax`) are pre-filled from the settlement preview and editable, and the
// new principal is principal + net. In Settled mode there are no settlement fields and the new
// principal is principal + the settled net. Control names follow the RollOverDepositRequest
// properties so a server 400 keyed on a field lands on it. The submit under test is called on the
// dialog itself and asserted on the raw request the generated client hands to `fetch`.
describe('RollOverDepositDialog', () => {
  let fixture: ComponentFixture<RollOverDepositDialog>;
  let component: RollOverDepositDialog;
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

  // The settlement preview answers with `dueDepositSettlementPreview` for a Due deposit and, as the
  // server does, with a 409 for a Settled one; every write answers with `writeResponse`.
  async function setup(
    deposit: DepositResponse,
    writeResponse: () => Response = () => jsonResponse(rolledOverDeposit),
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (requestMethod(input) === 'GET') {
        if (requestUrl(input).includes('/settlement-preview')) {
          return Number(deposit.status) === DEPOSIT_STATUS.Due
            ? jsonResponse(dueDepositSettlementPreview)
            : jsonResponse(
                {
                  detail: 'This deposit is already settled.',
                  errorCode: 'Conflict.DepositAlreadySettled',
                },
                409,
              );
        }
        return jsonResponse({ detail: 'Not found.' }, 404);
      }
      return writeResponse();
    });

    await TestBed.configureTestingModule({
      imports: [RollOverDepositDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: { deposit } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(RollOverDepositDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
    if (Number(deposit.status) === DEPOSIT_STATUS.Due) {
      // The settlement fields are pre-filled once the preview has loaded.
      await vi.waitFor(() =>
        expect(Number(findControl(form(), 'grossInterest').value)).toBe(
          dueDepositSettlementPreview.grossInterest,
        ),
      );
    }
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

  function renderedInput(controlName: string): Element | null {
    return (fixture.nativeElement as HTMLElement).querySelector(
      `[formcontrolname="${controlName}"]`,
    );
  }

  function mediumDate(year: number, month: number, day: number): string {
    return formatDate(new Date(year, month - 1, day), 'mediumDate', 'en-US');
  }

  const rolloverUrl = (deposit: DepositResponse) =>
    `/api/portfolio/portfolios/${deposit.portfolioId}/deposits/${deposit.assetId}/rollover`;

  describe('Due mode', () => {
    it('pre-fills gross and tax from the preview and the rate from the deposit', async () => {
      await setup(dueDeposit);

      const previewCall = fetchSpy.mock.calls
        .map((call: unknown[]) => requestUrl(call[0]))
        .find((url: string) => url.includes('/settlement-preview'));
      expect(previewCall).toContain(
        `/api/portfolio/portfolios/${dueDeposit.portfolioId}/deposits/${dueDeposit.assetId}/settlement-preview`,
      );
      expect(Number(findControl(form(), 'grossInterest').value)).toBe(147.95);
      expect(Number(findControl(form(), 'tax').value)).toBe(28.12);
      expect(Number(findControl(form(), 'annualInterestRatePercent').value)).toBe(6);
      expect(renderedInput('grossInterest')).not.toBeNull();
      expect(renderedInput('tax')).not.toBeNull();
      expect(renderedInput('annualInterestRatePercent')).not.toBeNull();
    });

    it('shows the title, the unchanged terms, the new principal, start date and "Matures on"', async () => {
      await setup(dueDeposit);

      const text = renderedText(fixture);
      expect(text).toMatch(/roll over/i);
      // The unchanged terms, read-only: 3 months, capitalised at maturity.
      expect(text).toMatch(/3\s*months/i);
      expect(text).toContain('At maturity');
      for (const readOnly of [
        'principal',
        'startDate',
        'termLength',
        'termUnit',
        'capitalization',
      ]) {
        expect(hasControl(form(), readOnly)).toBe(false);
      }
      // New principal = 10 000 + (147.95 − 28.12); new start = the old 2026-04-15 maturity; the next
      // term of 3 months matures 2026-07-15.
      expect(text).toContain(formatMoney(10119.83, 'PLN'));
      expect(text).toContain(mediumDate(2026, 4, 15));
      expect(text).toMatch(/matures on/i);
      expect(text).toContain(mediumDate(2026, 7, 15));
    });

    it('recomputes the new principal live as gross and tax change', async () => {
      await setup(dueDeposit);

      await fill({ grossInterest: 150, tax: 28.5 });

      const text = renderedText(fixture);
      expect(text).toContain(formatMoney(10121.5, 'PLN'));
      expect(text).not.toContain(formatMoney(10119.83, 'PLN'));
      expect(writeRequests(fetchSpy)).toEqual([]);
    });

    it.each([
      ['a negative rate', { annualInterestRatePercent: -0.5 }],
      ['a rate above 100', { annualInterestRatePercent: 100.5 }],
      ['no rate', { annualInterestRatePercent: null }],
      ['tax above gross', { grossInterest: 100, tax: 100.01 }],
      ['a negative gross', { grossInterest: -1, tax: 0 }],
      ['no tax', { tax: null }],
    ])('%s blocks submit', async (_case, values) => {
      await setup(dueDeposit);

      await fill(values);
      await component['onSubmit']();

      expect(form().invalid).toBe(true);
      expect(writeRequests(fetchSpy)).toEqual([]);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('submit POSTs the rate, gross and tax to rollover and closes with true', async () => {
      await setup(dueDeposit);
      await fill({ annualInterestRatePercent: 5.5 });

      await component['onSubmit']();

      const [request] = writeRequests(fetchSpy);
      expect(request.method).toBe('POST');
      expect(request.url).toContain(rolloverUrl(dueDeposit));
      expect(await request.json()).toEqual({
        annualInterestRatePercent: 5.5,
        grossInterest: 147.95,
        tax: 28.12,
      });
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });
  });

  describe('Settled mode', () => {
    it('shows no settlement fields and the new principal as principal + settled net', async () => {
      await setup(settledDeposit);

      expect(renderedInput('grossInterest')).toBeNull();
      expect(renderedInput('tax')).toBeNull();
      expect(renderedInput('annualInterestRatePercent')).not.toBeNull();
      expect(Number(findControl(form(), 'annualInterestRatePercent').value)).toBe(6);
      // 10 000 + (150.00 − 28.50) — the settled net, not the projected 119.83.
      const text = renderedText(fixture);
      expect(text).toContain(formatMoney(10121.5, 'PLN'));
      expect(text).not.toContain(formatMoney(10119.83, 'PLN'));
      // The next term starts on the old maturity date (not the 2026-04-17 settlement) and matures
      // 3 months later.
      expect(text).toContain(mediumDate(2026, 4, 15));
      expect(text).toContain(mediumDate(2026, 7, 15));
    });

    it('"Matures on" follows the deposit maturity math from the new start date', async () => {
      // A 1-month deposit that matured on 2026-01-31: its next term ends at the end of February.
      await setup(
        depositResponse({
          assetId: 'ffffffff-ffff-ffff-ffff-ffffffffffff',
          name: 'Month-end deposit',
          startDate: '2025-12-31',
          maturityDate: '2026-01-31',
          termLength: 1,
          status: DEPOSIT_STATUS.Settled,
          settledOn: '2026-01-31',
          settledGrossInterest: 50,
          settledTax: 9.5,
        } as Partial<DepositResponse>),
      );

      const text = renderedText(fixture);
      expect(text).toMatch(/matures on/i);
      expect(text).toContain(mediumDate(2026, 2, 28));
      expect(text).toContain(formatMoney(10040.5, 'PLN'));
    });

    it('an invalid rate blocks submit', async () => {
      await setup(settledDeposit);

      await fill({ annualInterestRatePercent: 101 });
      await component['onSubmit']();

      expect(writeRequests(fetchSpy)).toEqual([]);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('submit POSTs only the rate to rollover and closes with true', async () => {
      await setup(settledDeposit);
      await fill({ annualInterestRatePercent: 5.5 });

      await component['onSubmit']();

      const [request] = writeRequests(fetchSpy);
      expect(request.method).toBe('POST');
      expect(request.url).toContain(rolloverUrl(settledDeposit));
      const body = await request.json();
      expect(body).toEqual({ annualInterestRatePercent: 5.5 });
      expect(body).not.toHaveProperty('grossInterest');
      expect(body).not.toHaveProperty('tax');
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });
  });

  it('a 409 lands on the banner', async () => {
    await setup(dueDeposit, () =>
      jsonResponse(
        {
          detail: 'This deposit has not matured yet.',
          errorCode: 'Conflict.DepositNotDue',
        },
        409,
      ),
    );

    await component['onSubmit']();
    fixture.detectChanges();
    await fixture.whenStable();

    const banner = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(banner?.textContent).toContain('This deposit has not matured yet.');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('a server 400 keyed on the rate lands on the rate field', async () => {
    await setup(settledDeposit, () =>
      jsonResponse(
        {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: {
            AnnualInterestRatePercent: [
              'The interest rate can not have more than 4 decimal places.',
            ],
          },
        },
        400,
      ),
    );

    await component['onSubmit']();

    expect(findControl(form(), 'annualInterestRatePercent').hasError('server')).toBe(true);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with false on cancel', async () => {
    await setup(dueDeposit);

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  // i18n screens (#132) AC-6: the dialog's title, summary labels, field labels, validation messages
  // and buttons follow the language. (The read-only term, e.g. "3 months", is not asserted here.)
  describe('in Polish', () => {
    function errors(): string[] {
      return labelsOf(fixture.nativeElement as HTMLElement, 'mat-error');
    }

    async function read(values: Record<string, unknown>): Promise<string[]> {
      await fill(values);
      await component['onSubmit']();
      fixture.detectChanges();
      await fixture.whenStable();
      return errors();
    }

    it('renders in Polish', async () => {
      await setup(dueDeposit);
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...labelsOf(element, 'h2'),
        ...labelsOf(element, 'dt'),
        ...labelsOf(element, 'mat-label'),
        ...labelsOf(element, 'mat-dialog-actions button'),
      ];

      const english = texts();
      expect(english).toEqual([
        'Roll over',
        'Term',
        'Capitalisation',
        'New start date',
        'New principal',
        'Matures on',
        'Gross interest',
        'Tax',
        'Interest rate (%)',
        'Cancel',
        'Roll over',
      ]);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it('renders a Settled deposit in Polish', async () => {
      await setup(settledDeposit);
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...labelsOf(element, 'h2'),
        ...labelsOf(element, 'dt'),
        ...labelsOf(element, 'mat-label'),
        ...labelsOf(element, 'mat-dialog-actions button'),
      ];

      const english = texts();
      expect(english).toEqual([
        'Roll over',
        'Term',
        'Capitalisation',
        'New start date',
        'New principal',
        'Matures on',
        'Interest rate (%)',
        'Cancel',
        'Roll over',
      ]);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it('shows validation in Polish', async () => {
      await setup(dueDeposit);
      const scenarios: [string[], Record<string, unknown>][] = [
        [
          ['Gross interest is required.', 'Tax is required.', 'Interest rate is required.'],
          { grossInterest: null, tax: null, annualInterestRatePercent: null },
        ],
        [
          [
            "Gross interest can't be negative.",
            "Tax can't be negative.",
            'Interest rate must be between 0 and 100.',
          ],
          { grossInterest: -1, tax: -1, annualInterestRatePercent: 101 },
        ],
        [
          ["Tax can't exceed the gross interest."],
          { grossInterest: 10, tax: 20, annualInterestRatePercent: 5 },
        ],
      ];

      const english: string[][] = [];
      for (const [expected, values] of scenarios) {
        english.push(await read(values));
        expect(english.at(-1)).toEqual(expected);
      }

      await switchLanguage(fixture, 'pl');

      for (const [index, [, values]] of scenarios.entries()) {
        expect(polishProblems(english[index], await read(values))).toEqual([]);
      }
    });
  });
});
