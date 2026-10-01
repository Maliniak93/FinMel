import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { FormGroup } from '@angular/forms';
import { provideNativeDateAdapter } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { toDateOnly } from '../../../shared/date-only';
import { restoreEnglish } from '../../../../testing/i18n';
import {
  findControl,
  hasControl,
  jsonResponse,
  renderedText,
  requestUrl,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  archivedPortfolio,
  reservePortfolio,
  reservePortfolioId,
  savingsPortfolio,
  savingsPortfolioId,
  selectOptionLabels,
} from '../testing/deposit-fixtures';
import { savingsAccountResponse } from '../testing/savings-account-fixtures';
import {
  SavingsAccountFormDialog,
  type SavingsAccountFormDialogData,
} from './savings-account-form-dialog';

// savings-accounts AC-10. The create/edit dialog for a savings account: its controls are named after
// the AddSavingsAccountRequest / UpdateSavingsAccountRequest properties (camelCase) so a server 400
// keyed on a field lands on it; the optional opening deposit is the flat pair `openingAmount` +
// `openingDate`, sent as `openingDeposit: { amount, date }`. The submit under test is called on the
// dialog itself and asserted on the raw request the generated client hands to `fetch`.
describe('SavingsAccountFormDialog', () => {
  let fixture: ComponentFixture<SavingsAccountFormDialog>;
  let component: SavingsAccountFormDialog;
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

  function method(input: unknown): string {
    return typeof input === 'string' ? 'GET' : (input as Request).method;
  }

  // The portfolio list always answers with an archived portfolio in it, so the select has something
  // to leave out; every write answers with `writeResponse`.
  async function setup(
    data: SavingsAccountFormDialogData,
    writeResponse: () => Response = () => jsonResponse(savingsAccountResponse(), 201),
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (method(input) === 'GET' && /\/api\/portfolio\/portfolios(\?|$)/.test(requestUrl(input))) {
        return jsonResponse([savingsPortfolio, archivedPortfolio, reservePortfolio]);
      }
      return writeResponse();
    });

    await TestBed.configureTestingModule({
      imports: [SavingsAccountFormDialog],
      providers: [
        provideI18nTesting(),
        provideNativeDateAdapter(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(SavingsAccountFormDialog);
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

  function writeRequests(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0])
      .filter((input: unknown) => method(input) !== 'GET') as Request[];
  }

  function daysFromToday(days: number): Date {
    const date = new Date();
    date.setHours(0, 0, 0, 0);
    date.setDate(date.getDate() + days);
    return date;
  }

  // A complete, valid set of terms without an opening deposit.
  const validTerms = {
    name: 'Savings account',
    bankName: 'Test bank',
    currency: 'PLN',
    annualInterestRatePercent: 5.25,
    taxExempt: false,
  };

  describe('create', () => {
    it('offers only the non-archived portfolios', async () => {
      await setup({});

      const labels = await selectOptionLabels(fixture, 'portfolioId');

      expect(labels).toContain('Savings');
      expect(labels).toContain('Reserve');
      expect(labels).not.toContain('Old savings');
    });

    it('defaults the opening deposit date to today', async () => {
      await setup({});

      const date = findControl(form(), 'openingDate').value as Date;

      expect(toDateOnly(date)).toBe(toDateOnly(new Date()));
    });

    it('POSTs every field with the opening deposit and closes with true', async () => {
      await setup({});
      await fill({
        ...validTerms,
        portfolioId: reservePortfolioId,
        currency: 'EUR',
        taxExempt: true,
        openingAmount: 10000,
        openingDate: new Date(2026, 0, 15),
      });

      await component['onSubmit']();

      const [request] = writeRequests();
      expect(request.method).toBe('POST');
      expect(request.url).toContain(
        `/api/portfolio/portfolios/${reservePortfolioId}/savings-accounts`,
      );
      expect(await request.json()).toEqual({
        name: 'Savings account',
        bankName: 'Test bank',
        currency: 'EUR',
        annualInterestRatePercent: 5.25,
        taxExempt: true,
        openingDeposit: { amount: 10000, date: '2026-01-15' },
      });
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('sends the opening deposit only when an amount is entered', async () => {
      await setup({});
      await fill({ ...validTerms, portfolioId: savingsPortfolioId });

      await component['onSubmit']();

      const [request] = writeRequests();
      expect(request.method).toBe('POST');
      expect(request.url).toContain(
        `/api/portfolio/portfolios/${savingsPortfolioId}/savings-accounts`,
      );
      const body = await request.json();
      expect(body).not.toHaveProperty('openingDeposit');
      expect(body).not.toHaveProperty('portfolioId');
      expect(body).toMatchObject({ name: 'Savings account', currency: 'PLN', taxExempt: false });
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('preset to a portfolio (from the asset type picker) POSTs to that portfolio', async () => {
      await setup({ portfolioId: savingsPortfolioId });
      await fill({ ...validTerms, openingAmount: 500 });

      await component['onSubmit']();

      const [request] = writeRequests();
      expect(request.method).toBe('POST');
      expect(request.url).toContain(
        `/api/portfolio/portfolios/${savingsPortfolioId}/savings-accounts`,
      );
      const body = await request.json();
      expect(body.openingDeposit.amount).toBe(500);
      expect(body.openingDeposit.date).toBe(toDateOnly(new Date()));
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it.each([
      ['annualInterestRatePercent', -0.5],
      ['annualInterestRatePercent', 100.5],
      ['name', ''],
      ['openingAmount', 0],
      ['openingAmount', -10],
    ])('an invalid %s (%s) blocks submit', async (field, value) => {
      await setup({});
      await fill({ ...validTerms, portfolioId: savingsPortfolioId, openingAmount: 1000 });

      await fill({ [field]: value });
      await component['onSubmit']();

      expect(findControl(form(), field).invalid).toBe(true);
      expect(writeRequests()).toEqual([]);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('a rate of exactly 0 and exactly 100 is accepted', async () => {
      await setup({});
      await fill({ ...validTerms, portfolioId: savingsPortfolioId });

      await fill({ annualInterestRatePercent: 0 });
      await component['onSubmit']();
      await fill({ annualInterestRatePercent: 100 });
      await component['onSubmit']();

      expect(writeRequests()).toHaveLength(2);
    });

    it('an opening date later than today blocks submit', async () => {
      await setup({});
      await fill({
        ...validTerms,
        portfolioId: savingsPortfolioId,
        openingAmount: 1000,
        openingDate: daysFromToday(1),
      });

      await component['onSubmit']();

      expect(findControl(form(), 'openingDate').invalid).toBe(true);
      expect(writeRequests()).toEqual([]);

      // Today itself is fine.
      await fill({ openingDate: daysFromToday(0) });
      await component['onSubmit']();
      expect(writeRequests()).toHaveLength(1);
    });

    it('server 400 field errors land on the matching fields', async () => {
      await setup({}, () =>
        jsonResponse(
          {
            title: 'One or more validation errors occurred.',
            status: 400,
            errors: { AnnualInterestRatePercent: ['Rate must be between 0 and 100.'] },
          },
          400,
        ),
      );
      await fill({ ...validTerms, portfolioId: savingsPortfolioId });

      await component['onSubmit']();

      expect(writeRequests()).toHaveLength(1);
      expect(findControl(form(), 'annualInterestRatePercent').hasError('server')).toBe(true);
      expect(findControl(form(), 'name').hasError('server')).toBe(false);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('an error with no matching field lands on the banner', async () => {
      await setup({}, () =>
        jsonResponse(
          { detail: 'This portfolio is archived.', errorCode: 'Conflict.PortfolioArchived' },
          409,
        ),
      );
      await fill({ ...validTerms, portfolioId: savingsPortfolioId });

      await component['onSubmit']();
      fixture.detectChanges();
      await fixture.whenStable();

      const banner = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
      expect(banner?.textContent).toContain('This portfolio is archived.');
      expect(dialogRef.close).not.toHaveBeenCalled();
    });
  });

  describe('edit', () => {
    it('pre-fills the terms, hides currency and opening deposit, and PUTs only the terms', async () => {
      const account = savingsAccountResponse({
        name: 'Existing account',
        bankName: 'Old bank',
        annualInterestRatePercent: 4,
        taxExempt: true,
      });
      await setup({ account }, () => jsonResponse(account));

      expect(findControl(form(), 'name').value).toBe('Existing account');
      expect(findControl(form(), 'bankName').value).toBe('Old bank');
      expect(Number(findControl(form(), 'annualInterestRatePercent').value)).toBe(4);
      expect(findControl(form(), 'taxExempt').value).toBe(true);
      // Portfolio, currency and the opening deposit exist on create only.
      for (const absent of ['portfolioId', 'currency', 'openingAmount', 'openingDate']) {
        expect(hasControl(form(), absent)).toBe(false);
      }
      const element = fixture.nativeElement as HTMLElement;
      expect(element.querySelector('mat-select[formcontrolname="currency"]')).toBeNull();
      expect(element.querySelector('mat-select[formcontrolname="portfolioId"]')).toBeNull();

      await fill({ name: 'Renamed account', annualInterestRatePercent: 3.5, taxExempt: false });
      await component['onSubmit']();

      const [request] = writeRequests();
      expect(request.method).toBe('PUT');
      expect(request.url).toContain(
        `/api/portfolio/portfolios/${account.portfolioId}/savings-accounts/${account.assetId}`,
      );
      expect(await request.json()).toEqual({
        name: 'Renamed account',
        bankName: 'Old bank',
        annualInterestRatePercent: 3.5,
        taxExempt: false,
      });
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('an out-of-range rate blocks the PUT', async () => {
      const account = savingsAccountResponse();
      await setup({ account }, () => jsonResponse(account));

      await fill({ annualInterestRatePercent: 101 });
      await component['onSubmit']();

      expect(findControl(form(), 'annualInterestRatePercent').invalid).toBe(true);
      expect(writeRequests()).toEqual([]);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });
  });

  // deposit-payout-to-savings AC-9. Opened from the payout destination select with
  // `{currency, portfolioId}` the dialog creates an account for a payout: the currency is fixed and
  // read-only, the portfolio defaults to the deposit's and can still be changed, there is no
  // opening deposit, and success closes with the created account instead of `true`.
  describe('payout target mode', () => {
    const payoutData: SavingsAccountFormDialogData = {
      currency: 'PLN',
      portfolioId: savingsPortfolioId,
    };

    async function render(): Promise<void> {
      fixture.detectChanges();
      await fixture.whenStable();
    }

    it('shows the given currency read-only', async () => {
      await setup(payoutData);
      await render();

      if (hasControl(form(), 'currency')) {
        const currency = findControl(form(), 'currency');
        expect(currency.disabled).toBe(true);
        expect(currency.value).toBe('PLN');
      }
      expect(renderedText(fixture)).toContain('PLN');
      // Not a select the user could change.
      expect(
        (fixture.nativeElement as HTMLElement).querySelector(
          'mat-select[formcontrolname="currency"]',
        ),
      ).toBeNull();
    });

    it('defaults the portfolio to the given one and still offers the other active portfolios', async () => {
      await setup(payoutData);
      await render();

      expect(findControl(form(), 'portfolioId').value).toBe(savingsPortfolioId);
      const labels = await selectOptionLabels(fixture, 'portfolioId');
      expect(labels).toContain('Savings');
      expect(labels).toContain('Reserve');
      expect(labels).not.toContain('Old savings');
    });

    it('hides the opening deposit', async () => {
      await setup(payoutData);
      await render();

      const element = fixture.nativeElement as HTMLElement;
      expect(hasControl(form(), 'openingAmount')).toBe(false);
      expect(hasControl(form(), 'openingDate')).toBe(false);
      expect(element.querySelector('[formcontrolname="openingAmount"]')).toBeNull();
      expect(element.querySelector('[formcontrolname="openingDate"]')).toBeNull();
    });

    it('POSTs in the given currency to the default portfolio, with no opening deposit, and closes with the created account', async () => {
      const created = savingsAccountResponse({ assetId: 'abababab-abab-abab-abab-abababababab' });
      await setup(payoutData, () => jsonResponse(created, 201));
      await render();
      await fill({ name: 'Savings account', bankName: 'Test bank', annualInterestRatePercent: 5.25 });

      await component['onSubmit']();

      const [request] = writeRequests();
      expect(request.method).toBe('POST');
      expect(request.url).toContain(
        `/api/portfolio/portfolios/${savingsPortfolioId}/savings-accounts`,
      );
      const body = await request.json();
      expect(body).not.toHaveProperty('openingDeposit');
      expect(body).toMatchObject({ name: 'Savings account', currency: 'PLN' });
      expect(dialogRef.close).toHaveBeenCalledTimes(1);
      expect(dialogRef.close).toHaveBeenCalledWith(created);
    });

    it('creates the account in another portfolio when one is picked', async () => {
      await setup(payoutData);
      await render();
      await fill({
        name: 'Savings account',
        annualInterestRatePercent: 5.25,
        portfolioId: reservePortfolioId,
      });

      await component['onSubmit']();

      const [request] = writeRequests();
      expect(request.url).toContain(
        `/api/portfolio/portfolios/${reservePortfolioId}/savings-accounts`,
      );
      expect((await request.json()).currency).toBe('PLN');
    });

    it('plain create mode still closes with true', async () => {
      await setup({});
      await fill({ ...validTerms, portfolioId: savingsPortfolioId });

      await component['onSubmit']();

      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });
  });
});
