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
import { assetGroups, blueGroupId, isGroupListRequest } from '../../groups/testing/group-fixtures';
import {
  CAPITALIZATION,
  bankAccountsForDeposits,
  depositResponse,
  mBankAccount,
  mBankUsdAccount,
  nestAccount,
  pickSelectOption,
  rolledOverDeposit,
  selectOptionLabels,
  TERM_UNIT,
} from '../testing/deposit-fixtures';
import { DepositFormDialog, type DepositFormDialogData } from './deposit-form-dialog';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { labelsOf, polishProblems, restoreEnglish, switchLanguage } from '../../../../testing/i18n';

describe('DepositFormDialog', () => {
  let fixture: ComponentFixture<DepositFormDialog>;
  let component: DepositFormDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  function method(input: unknown): string {
    return typeof input === 'string' ? 'GET' : (input as Request).method;
  }

  async function setup(
    data: DepositFormDialogData,
    writeResponse: () => Response = () => jsonResponse(depositResponse(), 201),
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
      if (isGroupListRequest(input)) {
        return jsonResponse(assetGroups);
      }
      if (method(input) === 'GET' && requestUrl(input).includes('/api/portfolio/accounts')) {
        return jsonResponse(bankAccountsForDeposits);
      }
      return writeResponse();
    });

    await TestBed.configureTestingModule({
      imports: [DepositFormDialog],
      providers: [
        provideI18nTesting(),
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

  const validTerms = {
    name: 'Term deposit',
    bankName: 'Test bank',
    principal: 10000,
    startDate: new Date(2026, 0, 15),
    termLength: 3,
    termUnit: TERM_UNIT.Months,
    annualInterestRatePercent: 6,
    capitalization: CAPITALIZATION.AtMaturity,
    earlyBreakInterestLossPercent: 100,
  };

  function writeRequests(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0])
      .filter((input: unknown) => method(input) !== 'GET') as Request[];
  }

  function optionLabels(controlName: string): Promise<string[]> {
    return selectOptionLabels(fixture, controlName);
  }

  function mediumDate(year: number, month: number, day: number): string {
    return formatDate(new Date(year, month - 1, day), 'mediumDate', 'en-US');
  }

  it('shows the maturity date read-only and recomputes it as start and term change', async () => {
    await setup({});

    await fill({ startDate: new Date(2026, 0, 31), termLength: 1, termUnit: TERM_UNIT.Months });
    expect(renderedText(fixture)).toContain(mediumDate(2026, 2, 28));

    await fill({ termLength: 45, termUnit: TERM_UNIT.Days });
    expect(renderedText(fixture)).toContain(mediumDate(2026, 3, 17));
    expect(renderedText(fixture)).not.toContain(mediumDate(2026, 2, 28));

    await fill({ startDate: new Date(2026, 1, 1) });
    expect(renderedText(fixture)).toContain(mediumDate(2026, 3, 18));

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
    await fill({ ...validTerms, sourceAccountId: mBankAccount.id });

    await fill({ [field]: value });
    await component['onSubmit']();

    expect(findControl(form(), field).invalid).toBe(true);
    expect(writeRequests()).toEqual([]);
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('a term above 120 months or 3650 days blocks submit', async () => {
    await setup({});
    await fill({ ...validTerms, sourceAccountId: mBankAccount.id });

    await fill({ termLength: 121, termUnit: TERM_UNIT.Months });
    await component['onSubmit']();
    await fill({ termLength: 3651, termUnit: TERM_UNIT.Days });
    await component['onSubmit']();

    expect(writeRequests()).toEqual([]);

    await fill({ termLength: 3650, termUnit: TERM_UNIT.Days });
    await component['onSubmit']();
    expect(writeRequests()).toHaveLength(1);
  });

  it('create POSTs the terms with the source account and closes with true', async () => {
    await setup({});
    await fill({ ...validTerms, sourceAccountId: mBankAccount.id });

    await component['onSubmit']();

    const [request] = writeRequests();
    expect(request.method).toBe('POST');
    expect(request.url).toContain(`/api/portfolio/deposits`);
    const body = await request.json();
    expect(body).toMatchObject({
      name: 'Term deposit',
      bankName: 'Test bank',
      sourceAccountId: mBankAccount.id,
      principal: 10000,
      startDate: '2026-01-15',
      groupId: null,
      termLength: 3,
      termUnit: TERM_UNIT.Months,
      annualInterestRatePercent: 6,
      capitalization: CAPITALIZATION.AtMaturity,
      earlyBreakInterestLossPercent: 100,
    });
    for (const gone of ['currency', 'taxExempt', 'fundingAssetId']) {
      expect(body).not.toHaveProperty(gone);
    }
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  describe('group', () => {
    it('sends groupId', async () => {
      await setup({});
      await fill({ ...validTerms, sourceAccountId: mBankAccount.id });
      await pickSelectOption(fixture, 'groupId', 'Emerytura');

      await component['onSubmit']();

      const [request] = writeRequests();
      expect((await request.json()).groupId).toBe(blueGroupId);
    });
  });

  it('edit pre-fills the terms and PUTs them without currency', async () => {
    const deposit = depositResponse({
      name: 'Existing deposit',
      capitalization: CAPITALIZATION.Monthly,
      earlyBreakInterestLossPercent: 50,
    });
    await setup({ deposit }, () => jsonResponse(deposit));

    expect(findControl(form(), 'name').value).toBe('Existing deposit');
    expect(findControl(form(), 'bankName').value).toBe('Test bank');
    expect(Number(findControl(form(), 'principal').value)).toBe(10000);
    expect(Number(findControl(form(), 'termLength').value)).toBe(3);
    expect(findControl(form(), 'capitalization').value).toBe(CAPITALIZATION.Monthly);
    expect(findControl(form(), 'returnAccountId').value).toBe(mBankAccount.id);
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelector('mat-select[formcontrolname="currency"]')).toBeNull();
    expect(element.querySelector('mat-select[formcontrolname="sourceAccountId"]')).toBeNull();
    expect(renderedText(fixture)).toContain(mediumDate(2026, 4, 15));

    await fill({ principal: 15000, startDate: new Date(2026, 1, 1), termLength: 6 });
    await component['onSubmit']();

    const [request] = writeRequests();
    expect(request.method).toBe('PUT');
    expect(request.url).toContain(`/api/portfolio/deposits/${deposit.assetId}`);
    const body = await request.json();
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
      returnAccountId: mBankAccount.id,
      earlyBreakInterestLossPercent: 50,
    });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('edit of a rolled-over deposit disables principal and start date, and the PUT still carries them', async () => {
    await setup({ deposit: rolledOverDeposit }, () => jsonResponse(rolledOverDeposit));

    expect(findControl(form(), 'principal').disabled).toBe(true);
    expect(findControl(form(), 'startDate').disabled).toBe(true);
    for (const editable of [
      'name',
      'bankName',
      'termLength',
      'termUnit',
      'annualInterestRatePercent',
      'capitalization',
      'earlyBreakInterestLossPercent',
    ]) {
      expect(findControl(form(), editable).enabled).toBe(true);
    }
    const principalInput = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>(
      'input[formcontrolname="principal"]',
    );
    expect(principalInput?.disabled).toBe(true);

    await fill({ name: 'Renamed deposit', annualInterestRatePercent: 4.5, termLength: 6 });
    expect(renderedText(fixture)).toContain(mediumDate(2026, 10, 15));
    await component['onSubmit']();

    const [request] = writeRequests();
    expect(request.method).toBe('PUT');
    expect(request.url).toContain(`/api/portfolio/deposits/${rolledOverDeposit.assetId}`);
    expect(await request.json()).toEqual({
      name: 'Renamed deposit',
      bankName: 'Bank G',
      principal: 10119.83,
      startDate: '2026-04-15',
      termLength: 6,
      termUnit: TERM_UNIT.Months,
      annualInterestRatePercent: 4.5,
      capitalization: CAPITALIZATION.AtMaturity,
      returnAccountId: mBankAccount.id,
      earlyBreakInterestLossPercent: 100,
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
    await fill({ ...validTerms, sourceAccountId: mBankAccount.id });

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
          detail: 'This asset is archived.',
          errorCode: 'Conflict.AssetArchived',
        },
        409,
      ),
    );
    await fill({ ...validTerms, sourceAccountId: mBankAccount.id });

    await component['onSubmit']();
    fixture.detectChanges();
    await fixture.whenStable();

    const banner = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(banner?.textContent).toContain('This asset is archived.');
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('closes with false on cancel', async () => {
    await setup({});

    component['cancel']();

    expect(dialogRef.close).toHaveBeenCalledWith(false);
  });

  describe('source account', () => {
    function accountRequests(): URL[] {
      return fetchSpy.mock.calls
        .map((call: unknown[]) => call[0])
        .filter(
          (input: unknown) =>
            method(input) === 'GET' && requestUrl(input).includes('/api/portfolio/accounts'),
        )
        .map((input: unknown) => new URL(requestUrl(input)));
    }

    function balancePattern(amount: number): RegExp {
      const digits = String(amount);
      const grouped = digits.replace(/\B(?=(\d{3})+(?!\d))/g, '[\\s,.\\u00a0\\u202f]?');
      return new RegExp(grouped);
    }

    it('offers the unarchived bank accounts with name, currency and balance, and no funding or tax controls', async () => {
      await setup({});

      const requests = accountRequests();
      expect(requests.length).toBeGreaterThan(0);
      expect(['Bank', '2']).toContain(requests.at(-1)!.searchParams.get('type'));

      const labels = await optionLabels('sourceAccountId');
      expect(labels).toHaveLength(3);
      const mBank = labels.find((label) => label.includes('mBank') && !label.includes('USD'));
      expect(mBank).toContain('PLN');
      expect(mBank).toMatch(balancePattern(20000));
      const usd = labels.find((label) => label.includes('mBank USD'));
      expect(usd).toContain('USD');
      expect(usd).toMatch(balancePattern(1000));
      expect(labels.some((label) => label.includes(nestAccount.name))).toBe(true);
      expect(labels.some((label) => label.includes('Old savings'))).toBe(false);

      const element = fixture.nativeElement as HTMLElement;
      expect(hasControl(form(), 'fundingAssetId')).toBe(false);
      expect(hasControl(form(), 'taxExempt')).toBe(false);
      expect(element.querySelector('mat-slide-toggle')).toBeNull();
      expect(element.querySelector('mat-select[formcontrolname="currency"]')).toBeNull();
    });

    it('a source account is required to submit', async () => {
      await setup({});
      await fill(validTerms);

      await component['onSubmit']();

      expect(findControl(form(), 'sourceAccountId').invalid).toBe(true);
      expect(writeRequests()).toEqual([]);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('picking a source offers return accounts in its currency only', async () => {
      await setup({});

      await fill({ sourceAccountId: mBankAccount.id });
      const plnLabels = await optionLabels('returnAccountId');
      expect(plnLabels.some((label) => label.includes(nestAccount.name))).toBe(true);
      expect(plnLabels.some((label) => label.includes('USD'))).toBe(false);

      await fill({ sourceAccountId: mBankUsdAccount.id });
      const usdLabels = await optionLabels('returnAccountId');
      expect(usdLabels.some((label) => label.includes(nestAccount.name))).toBe(false);
      expect(renderedText(fixture)).toContain('USD');
    });

    it('changing the source drops a return account in another currency', async () => {
      await setup({});
      await fill({ sourceAccountId: mBankAccount.id, returnAccountId: nestAccount.id });

      await fill({ sourceAccountId: mBankUsdAccount.id });

      expect(findControl(form(), 'returnAccountId').value).not.toBe(nestAccount.id);
    });

    it('without a chosen return account the POST names the source or leaves it out', async () => {
      await setup({});
      await fill({ ...validTerms, sourceAccountId: mBankAccount.id });

      await component['onSubmit']();

      const [request] = writeRequests();
      const body = await request.json();
      expect(body.sourceAccountId).toBe(mBankAccount.id);
      expect([undefined, null, mBankAccount.id]).toContain(body.returnAccountId);
    });

    it('create POSTs the chosen return account', async () => {
      await setup({});
      await fill({
        ...validTerms,
        sourceAccountId: mBankAccount.id,
        returnAccountId: nestAccount.id,
      });

      await component['onSubmit']();

      const [request] = writeRequests();
      expect(request.method).toBe('POST');
      const body = await request.json();
      expect(body.sourceAccountId).toBe(mBankAccount.id);
      expect(body.returnAccountId).toBe(nestAccount.id);
      expect(dialogRef.close).toHaveBeenCalledWith(true);
    });

    it('a server 400 InsufficientFunds shows "Brak środków na konto" inline', async () => {
      await setup({}, () =>
        jsonResponse(
          {
            title: 'Validation Failed',
            status: 400,
            detail: 'The source of funds cannot cover this principal.',
            errorCode: 'Validation.InsufficientFunds',
          },
          400,
        ),
      );
      await fill({ ...validTerms, sourceAccountId: mBankAccount.id, principal: 30000 });
      await switchLanguage(fixture, 'pl');

      await component['onSubmit']();
      fixture.detectChanges();
      await fixture.whenStable();

      const messages = labelsOf(fixture.nativeElement as HTMLElement, 'mat-error');
      expect(messages.some((message) => message.includes('Brak środków na koncie'))).toBe(true);
      expect(dialogRef.close).not.toHaveBeenCalled();
    });

    it('edit offers only the return account and the PUT omits the source', async () => {
      const deposit = depositResponse({
        returnAccountId: mBankAccount.id,
        returnAccountName: mBankAccount.name,
      });
      await setup({ deposit }, () => jsonResponse(deposit));

      expect(
        (fixture.nativeElement as HTMLElement).querySelector(
          'mat-select[formcontrolname="sourceAccountId"]',
        ),
      ).toBeNull();
      expect(renderedText(fixture)).toContain(mBankAccount.name);

      await fill({ returnAccountId: nestAccount.id });
      await component['onSubmit']();

      const [request] = writeRequests();
      expect(request.method).toBe('PUT');
      const body = await request.json();
      expect(body.returnAccountId).toBe(nestAccount.id);
      expect(body).not.toHaveProperty('sourceAccountId');
    });
  });

  describe('in Polish', () => {
    function errors(): string[] {
      return labelsOf(fixture.nativeElement as HTMLElement, 'mat-error');
    }

    async function touchAll(): Promise<void> {
      await component['onSubmit']();
      fixture.detectChanges();
      await fixture.whenStable();
    }

    it('shows required-field validation in Polish', async () => {
      await setup({});
      await fill({ sourceAccountId: mBankAccount.id });
      await touchAll();

      const english = errors();
      expect(english).toEqual([
        'Name is required.',
        'Principal is required.',
        'Term is required.',
        'Interest rate is required.',
      ]);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, errors())).toEqual([]);
    });

    it('shows range and length validation in Polish', async () => {
      await setup({});
      await fill({
        sourceAccountId: mBankAccount.id,
        name: 'a'.repeat(201),
        bankName: 'a'.repeat(101),
        principal: 0,
        startDate: null,
        termLength: 121,
        annualInterestRatePercent: 101,
        earlyBreakInterestLossPercent: 101,
      });
      await touchAll();

      const english = errors();
      expect(english).toEqual([
        'Name is too long.',
        'Bank name is too long.',
        'Principal must be greater than 0.',
        'Start date is required.',
        'Term is at most 120.',
        'Interest rate must be between 0 and 100.',
        'Early-break loss must be between 0 and 100.',
      ]);

      await switchLanguage(fixture, 'pl');

      const polish = errors();
      expect(polishProblems(english, polish)).toEqual([]);
      expect(polish[4]).toContain('120');
    });

    it('renders the source and return account fields in Polish', async () => {
      await setup({});

      await switchLanguage(fixture, 'pl');

      const text = renderedText(fixture);
      expect(text).toContain('Z konta');
      expect(text).toContain('Zwrot na');
      expect(text).not.toContain('IKE/IKZE');
    });
  });
});
