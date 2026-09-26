import { OverlayContainer } from '@angular/cdk/overlay';
import { formatDate } from '@angular/common';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import {
  findControl,
  hasControl,
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  archivedPortfolio,
  CAPITALIZATION,
  depositResponse,
  reservePortfolio,
  reservePortfolioId,
  savingsPortfolio,
  savingsPortfolioId,
  TERM_UNIT,
} from '../testing/deposit-fixtures';
import { DepositFormDialog, type DepositFormDialogData } from './deposit-form-dialog';

// term-deposits AC-14. The create/edit dialog for a term deposit: its controls are named after the
// AddDepositRequest/UpdateDepositRequest properties (camelCase) so a server 400 keyed on a field
// lands on it. The submit under test is called on the dialog itself and asserted on the raw
// request the generated client hands to `fetch`.
describe('DepositFormDialog', () => {
  let fixture: ComponentFixture<DepositFormDialog>;
  let component: DepositFormDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  function method(input: unknown): string {
    return typeof input === 'string' ? 'GET' : (input as Request).method;
  }

  // The portfolio list (GET /api/portfolio/portfolios) always answers with an archived portfolio in
  // it, so the select has something to leave out; every write answers with `writeResponse`.
  async function setup(
    data: DepositFormDialogData,
    writeResponse: () => Response = () => jsonResponse(depositResponse(), 201),
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (method(input) === 'GET' && /\/api\/portfolio\/portfolios(\?|$)/.test(requestUrl(input))) {
        return jsonResponse([savingsPortfolio, archivedPortfolio, reservePortfolio]);
      }
      return writeResponse();
    });

    await TestBed.configureTestingModule({
      imports: [DepositFormDialog],
      providers: [
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(DepositFormDialog);
    component = fixture.componentInstance;
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

  // A complete, valid set of terms — the spec's AC-1 deposit.
  const validTerms = {
    name: 'Term deposit',
    bankName: 'Test bank',
    principal: 10000,
    startDate: new Date(2026, 0, 15),
    termLength: 3,
    termUnit: TERM_UNIT.Months,
    annualInterestRatePercent: 6,
    capitalization: CAPITALIZATION.AtMaturity,
    taxExempt: false,
    earlyBreakInterestLossPercent: 100,
  };

  function writeRequests(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0])
      .filter((input: unknown) => method(input) !== 'GET') as Request[];
  }

  async function optionLabels(controlName: string): Promise<string[]> {
    const trigger = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      `mat-select[formcontrolname="${controlName}"] .mat-mdc-select-trigger`,
    );
    if (!trigger) {
      throw new Error(`No ${controlName} select rendered.`);
    }
    trigger.click();
    fixture.detectChanges();
    await fixture.whenStable();
    return Array.from(
      TestBed.inject(OverlayContainer).getContainerElement().querySelectorAll('mat-option'),
      (option) => (option.textContent ?? '').trim(),
    );
  }

  function mediumDate(year: number, month: number, day: number): string {
    return formatDate(new Date(year, month - 1, day), 'mediumDate', 'en-US');
  }

  it('shows the maturity date read-only and recomputes it as start and term change', async () => {
    await setup({});

    await fill({ startDate: new Date(2026, 0, 31), termLength: 1, termUnit: TERM_UNIT.Months });
    // 2026-01-31 + 1 month clamps to the end of February.
    expect(renderedText(fixture)).toContain(mediumDate(2026, 2, 28));

    await fill({ termLength: 45, termUnit: TERM_UNIT.Days });
    expect(renderedText(fixture)).toContain(mediumDate(2026, 3, 17));
    expect(renderedText(fixture)).not.toContain(mediumDate(2026, 2, 28));

    await fill({ startDate: new Date(2026, 1, 1) });
    // 2026-02-01 + 45 days.
    expect(renderedText(fixture)).toContain(mediumDate(2026, 3, 18));

    // Read-only: it is shown, never an input the user could type into.
    expect(hasControl(form(), 'maturityDate')).toBe(false);
    expect(fetchSpy.mock.calls.every((call: unknown[]) => method(call[0]) === 'GET')).toBe(true);
  });

  it.each([
    ['principal', 0],
    ['principal', -100],
    ['annualInterestRatePercent', -0.5],
    ['annualInterestRatePercent', 100.5],
    ['termLength', 0],
    ['earlyBreakInterestLossPercent', -1],
    ['earlyBreakInterestLossPercent', 100.5],
    ['name', ''],
  ])('an invalid %s (%s) blocks submit', async (field, value) => {
    await setup({});
    await fill({ ...validTerms, portfolioId: savingsPortfolioId, currency: 'PLN' });

    await fill({ [field]: value });
    await component['onSubmit']();

    expect(findControl(form(), field).invalid).toBe(true);
    expect(writeRequests()).toEqual([]);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('a term above 120 months or 3650 days blocks submit', async () => {
    await setup({});
    await fill({ ...validTerms, portfolioId: savingsPortfolioId, currency: 'PLN' });

    await fill({ termLength: 121, termUnit: TERM_UNIT.Months });
    await component['onSubmit']();
    await fill({ termLength: 3651, termUnit: TERM_UNIT.Days });
    await component['onSubmit']();

    expect(writeRequests()).toEqual([]);

    // The same number of days is fine once it fits.
    await fill({ termLength: 3650, termUnit: TERM_UNIT.Days });
    await component['onSubmit']();
    expect(writeRequests()).toHaveLength(1);
  });

  it('offers only the non-archived portfolios on create', async () => {
    await setup({});

    const labels = await optionLabels('portfolioId');

    expect(labels).toContain('Savings');
    expect(labels).toContain('Reserve');
    expect(labels).not.toContain('Old savings');
  });

  it('create POSTs the terms to the chosen portfolio and closes with true', async () => {
    await setup({});
    await fill({ ...validTerms, portfolioId: reservePortfolioId, currency: 'EUR' });

    await component['onSubmit']();

    const [request] = writeRequests();
    expect(request.method).toBe('POST');
    expect(request.url).toContain(`/api/portfolio/portfolios/${reservePortfolioId}/deposits`);
    expect(await request.json()).toEqual({
      name: 'Term deposit',
      bankName: 'Test bank',
      currency: 'EUR',
      principal: 10000,
      startDate: '2026-01-15',
      termLength: 3,
      termUnit: TERM_UNIT.Months,
      annualInterestRatePercent: 6,
      capitalization: CAPITALIZATION.AtMaturity,
      taxExempt: false,
      earlyBreakInterestLossPercent: 100,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('create preset to a portfolio (from the asset type picker) POSTs to that portfolio', async () => {
    await setup({ portfolioId: savingsPortfolioId });
    await fill({ ...validTerms, currency: 'PLN', taxExempt: true });

    await component['onSubmit']();

    const [request] = writeRequests();
    expect(request.method).toBe('POST');
    expect(request.url).toContain(`/api/portfolio/portfolios/${savingsPortfolioId}/deposits`);
    const body = await request.json();
    expect(body.taxExempt).toBe(true);
    expect(body).not.toHaveProperty('portfolioId');
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('edit pre-fills the terms and PUTs them without portfolio or currency', async () => {
    const deposit = depositResponse({
      name: 'Existing deposit',
      capitalization: CAPITALIZATION.Monthly,
      taxExempt: true,
      earlyBreakInterestLossPercent: 50,
    });
    await setup({ deposit }, () => jsonResponse(deposit));

    expect(findControl(form(), 'name').value).toBe('Existing deposit');
    expect(findControl(form(), 'bankName').value).toBe('Test bank');
    expect(Number(findControl(form(), 'principal').value)).toBe(10000);
    expect(Number(findControl(form(), 'termLength').value)).toBe(3);
    expect(findControl(form(), 'capitalization').value).toBe(CAPITALIZATION.Monthly);
    expect(findControl(form(), 'taxExempt').value).toBe(true);
    // Portfolio and currency are fixed after create: no select for either.
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('mat-select[formcontrolname="portfolioId"]')).toBeNull();
    expect(element.querySelector('mat-select[formcontrolname="currency"]')).toBeNull();
    expect(renderedText(fixture)).toContain(mediumDate(2026, 4, 15));

    await fill({ principal: 15000, startDate: new Date(2026, 1, 1), termLength: 6 });
    await component['onSubmit']();

    const [request] = writeRequests();
    expect(request.method).toBe('PUT');
    expect(request.url).toContain(
      `/api/portfolio/portfolios/${deposit.portfolioId}/deposits/${deposit.assetId}`,
    );
    const body = await request.json();
    expect(body).not.toHaveProperty('portfolioId');
    expect(body).not.toHaveProperty('currency');
    expect(body).toEqual({
      name: 'Existing deposit',
      bankName: 'Test bank',
      principal: 15000,
      startDate: '2026-02-01',
      termLength: 6,
      termUnit: TERM_UNIT.Months,
      annualInterestRatePercent: 6,
      capitalization: CAPITALIZATION.Monthly,
      taxExempt: true,
      earlyBreakInterestLossPercent: 50,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('server 400 field errors land on the matching fields', async () => {
    await setup({}, () =>
      jsonResponse(
        {
          title: 'One or more validation errors occurred.',
          status: 400,
          errors: {
            Principal: ['The principal must be greater than 0.'],
            TermLength: ['A term is at most 120 months.'],
            EarlyBreakInterestLossPercent: ['Must be between 0 and 100.'],
          },
        },
        400,
      ),
    );
    await fill({ ...validTerms, portfolioId: savingsPortfolioId, currency: 'PLN' });

    await component['onSubmit']();

    expect(findControl(form(), 'principal').hasError('server')).toBe(true);
    expect(findControl(form(), 'termLength').hasError('server')).toBe(true);
    expect(findControl(form(), 'earlyBreakInterestLossPercent').hasError('server')).toBe(true);
    expect(findControl(form(), 'name').hasError('server')).toBe(false);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('an error with no matching field lands on the banner', async () => {
    await setup({}, () =>
      jsonResponse(
        {
          detail: 'This portfolio is archived.',
          errorCode: 'Conflict.PortfolioArchived',
        },
        409,
      ),
    );
    await fill({ ...validTerms, portfolioId: savingsPortfolioId, currency: 'PLN' });

    await component['onSubmit']();
    fixture.detectChanges();
    await fixture.whenStable();

    const banner = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(banner?.textContent).toContain('This portfolio is archived.');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with false on cancel', async () => {
    await setup({});

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });
});
