import { OverlayContainer } from '@angular/cdk/overlay';
import { TestBed, type ComponentFixture } from '@angular/core/testing';

import type { DepositResponse, PortfolioResponse } from '../../../api/portfolio';
import { requestUrl } from '../../assets/asset-form/testing/asset-form-fixtures';

export const savingsPortfolioId = '22222222-2222-2222-2222-222222222222';
export const reservePortfolioId = '33333333-3333-3333-3333-333333333333';
export const archivedPortfolioId = '44444444-4444-4444-4444-444444444444';

export const savingsPortfolio: PortfolioResponse = {
  id: savingsPortfolioId,
  name: 'Savings',
  description: null,
  currency: 'PLN',
  isArchived: false,
  assetCount: 1,
};

export const reservePortfolio: PortfolioResponse = {
  ...savingsPortfolio,
  id: reservePortfolioId,
  name: 'Reserve',
};

export const archivedPortfolio: PortfolioResponse = {
  ...savingsPortfolio,
  id: archivedPortfolioId,
  name: 'Old savings',
  isArchived: true,
};

export const TERM_UNIT = { Days: 0, Months: 1 } as const;
export const CAPITALIZATION = { AtMaturity: 0, Monthly: 1, Quarterly: 2, Yearly: 3 } as const;
export const DEPOSIT_STATUS = { Active: 0, Due: 1, Settled: 2, PaidOut: 3 } as const;

export function depositResponse(overrides: Partial<DepositResponse> = {}): DepositResponse {
  return {
    assetId: '11111111-1111-1111-1111-111111111111',
    portfolioId: savingsPortfolioId,
    portfolioName: 'Savings',
    portfolioIsArchived: false,
    isArchived: false,
    name: 'Term deposit',
    bankName: 'Test bank',
    currency: 'PLN',
    principal: 10000,
    startDate: '2026-01-15',
    termLength: 3,
    termUnit: TERM_UNIT.Months,
    maturityDate: '2026-04-15',
    annualInterestRatePercent: 6,
    capitalization: CAPITALIZATION.AtMaturity,
    taxExempt: false,
    earlyBreakInterestLossPercent: 100,
    projection: {
      grossInterest: 147.95,
      tax: 28.12,
      netInterest: 119.83,
      finalAmount: 10119.83,
      netProfitPercent: 1.1983,
    },
    status: DEPOSIT_STATUS.Active,
    rolloverCount: 0,
    ...overrides,
  } as DepositResponse;
}

export const activeDeposit: DepositResponse = depositResponse({
  assetId: '55555555-5555-5555-5555-555555555555',
  portfolioId: reservePortfolioId,
  portfolioName: 'Reserve',
  name: 'Running deposit',
  bankName: 'Bank A',
  principal: 12000,
  startDate: '2026-09-01',
  maturityDate: '2027-09-01',
  termLength: 12,
  annualInterestRatePercent: 5.5,
  projection: {
    grossInterest: 660,
    tax: 125.4,
    netInterest: 534.6,
    finalAmount: 12534.6,
    netProfitPercent: 4.455,
  },
  status: DEPOSIT_STATUS.Active,
} as Partial<DepositResponse>);

export const dueDeposit: DepositResponse = depositResponse({
  assetId: '66666666-6666-6666-6666-666666666666',
  name: 'Matured deposit',
  bankName: 'Bank B',
  status: DEPOSIT_STATUS.Due,
});

export const archivedPortfolioDeposit: DepositResponse = depositResponse({
  assetId: '77777777-7777-7777-7777-777777777777',
  portfolioId: archivedPortfolioId,
  portfolioName: 'Old savings',
  portfolioIsArchived: true,
  name: 'Archived deposit',
  bankName: 'Bank C',
});

export const archivedDueDeposit: DepositResponse = depositResponse({
  assetId: 'f1f1f1f1-f1f1-f1f1-f1f1-f1f1f1f1f1f1',
  name: 'Shelved due deposit',
  bankName: 'Bank H',
  status: DEPOSIT_STATUS.Due,
  isArchived: true,
} as Partial<DepositResponse>);

export const archivedSettledDeposit: DepositResponse = depositResponse({
  assetId: 'f2f2f2f2-f2f2-f2f2-f2f2-f2f2f2f2f2f2',
  name: 'Shelved settled deposit',
  bankName: 'Bank I',
  status: DEPOSIT_STATUS.Settled,
  settledOn: '2026-04-17',
  settledGrossInterest: 150,
  settledTax: 28.5,
  isArchived: true,
} as Partial<DepositResponse>);

export const settledDeposit: DepositResponse = depositResponse({
  assetId: '88888888-8888-8888-8888-888888888888',
  name: 'Settled deposit',
  bankName: 'Bank D',
  status: DEPOSIT_STATUS.Settled,
  settledOn: '2026-04-17',
  settledGrossInterest: 150,
  settledTax: 28.5,
} as Partial<DepositResponse>);

export const settledDepositFinalAmount = 10121.5;

export const paidOutDepositDestinationName = 'Current account';

export const paidOutDeposit: DepositResponse = depositResponse({
  assetId: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
  name: 'Paid-out deposit',
  bankName: 'Bank E',
  status: DEPOSIT_STATUS.PaidOut,
  settledOn: '2026-04-17',
  settledGrossInterest: 150,
  settledTax: 28.5,
  paidOutOn: '2026-04-20',
  paidOutToAssetName: paidOutDepositDestinationName,
} as Partial<DepositResponse>);

export const paidOutDepositWithRemovedDestination: DepositResponse = depositResponse({
  assetId: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
  name: 'Orphaned payout',
  bankName: 'Bank F',
  status: DEPOSIT_STATUS.PaidOut,
  settledOn: '2026-04-17',
  settledGrossInterest: 150,
  settledTax: 28.5,
  paidOutOn: '2026-04-20',
  paidOutToAssetName: null,
} as Partial<DepositResponse>);

export const paidOutIntoSavingsDeposit: DepositResponse = depositResponse({
  assetId: 'c1c1c1c1-c1c1-c1c1-c1c1-c1c1c1c1c1c1',
  name: 'Deposit paid into savings',
  bankName: 'Bank J',
  status: DEPOSIT_STATUS.PaidOut,
  settledOn: '2026-04-17',
  settledGrossInterest: 150,
  settledTax: 28.5,
  paidOutOn: '2026-04-20',
  paidOutToAssetName: 'Emergency fund',
} as Partial<DepositResponse>);

export const rolledOverDeposit: DepositResponse = depositResponse({
  assetId: 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee',
  name: 'Rolled-over deposit',
  bankName: 'Bank G',
  principal: 10119.83,
  startDate: '2026-04-15',
  maturityDate: '2026-07-15',
  annualInterestRatePercent: 5.5,
  status: DEPOSIT_STATUS.Active,
  rolloverCount: 1,
} as Partial<DepositResponse>);

export interface TransferCandidateFixture {
  assetId: string;
  name: string;
  portfolioId: string;
  portfolioName: string;
  balance: number;
}

export const walletPortfolioId = '99999999-9999-9999-9999-999999999999';

export const plnCashCandidate: TransferCandidateFixture = {
  assetId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  name: 'Cash account',
  portfolioId: walletPortfolioId,
  portfolioName: 'Wallet',
  balance: 5000,
};

export const eurCashCandidate: TransferCandidateFixture = {
  assetId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  name: 'EUR account',
  portfolioId: walletPortfolioId,
  portfolioName: 'Wallet',
  balance: 2500,
};

export const transferCandidatesByCurrency: Record<string, TransferCandidateFixture[]> = {
  PLN: [plnCashCandidate],
  EUR: [eurCashCandidate],
};

export const reserveSavingsPortfolioId = '12121212-1212-1212-1212-121212121212';

export const plnSavingsCandidate: TransferCandidateFixture = {
  assetId: 'cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd',
  name: 'Emergency fund',
  portfolioId: reserveSavingsPortfolioId,
  portfolioName: 'Reserve savings',
  balance: 750,
};

export const eurSavingsCandidate: TransferCandidateFixture = {
  assetId: 'efefefef-efef-efef-efef-efefefefefef',
  name: 'EUR savings',
  portfolioId: reserveSavingsPortfolioId,
  portfolioName: 'Reserve savings',
  balance: 300,
};

export const savingsCandidatesByCurrency: Record<string, TransferCandidateFixture[]> = {
  PLN: [plnSavingsCandidate],
  EUR: [eurSavingsCandidate],
};

export function transferCandidatesFor(
  url: URL,
  cash: Record<string, TransferCandidateFixture[]> = transferCandidatesByCurrency,
  savings: Record<string, TransferCandidateFixture[]> = savingsCandidatesByCurrency,
): TransferCandidateFixture[] {
  const currency = url.searchParams.get('currency') ?? '';
  const assetClass = url.searchParams.get('assetClass');
  const bySavings = assetClass === 'Savings' || assetClass === '9';
  return (bySavings ? savings : cash)[currency] ?? [];
}

export function requestedCandidateClasses(fetchSpy: { mock: { calls: unknown[][] } }): string[] {
  return transferCandidateRequests(fetchSpy).map((url) => {
    const value = url.searchParams.get('assetClass');
    return value === '0' ? 'Cash' : value === '9' ? 'Savings' : (value ?? '');
  });
}

export const dueDepositSettlementPreview = {
  settledOn: '2026-04-15',
  grossInterest: 147.95,
  tax: 28.12,
  netInterest: 119.83,
  finalAmount: 10119.83,
};

export function requestMethod(input: unknown): string {
  return typeof input === 'string' ? 'GET' : (input as Request).method;
}

export function writeRequests(fetchSpy: { mock: { calls: unknown[][] } }): Request[] {
  return fetchSpy.mock.calls
    .map((call) => call[0])
    .filter((input) => requestMethod(input) !== 'GET') as Request[];
}

export function transferCandidateRequests(fetchSpy: { mock: { calls: unknown[][] } }): URL[] {
  return fetchSpy.mock.calls
    .map((call) => call[0])
    .filter(
      (input) =>
        requestMethod(input) === 'GET' &&
        requestUrl(input).includes('/api/portfolio/transfer-candidates'),
    )
    .map((input) => new URL(requestUrl(input)));
}

async function openSelectOptions(
  fixture: ComponentFixture<unknown>,
  controlName: string,
): Promise<HTMLElement[]> {
  const trigger = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
    `mat-select[formcontrolname="${controlName}"] .mat-mdc-select-trigger, [formcontrolname="${controlName}"] mat-select .mat-mdc-select-trigger`,
  );
  if (!trigger) {
    throw new Error(`No ${controlName} select rendered.`);
  }
  trigger.click();
  fixture.detectChanges();
  await fixture.whenStable();
  return Array.from(
    TestBed.inject(OverlayContainer)
      .getContainerElement()
      .querySelectorAll<HTMLElement>('mat-option'),
  );
}

export async function selectOptionLabels(
  fixture: ComponentFixture<unknown>,
  controlName: string,
): Promise<string[]> {
  const options = await openSelectOptions(fixture, controlName);
  return options.map((option) => (option.textContent ?? '').trim());
}

export async function pickSelectOption(
  fixture: ComponentFixture<unknown>,
  controlName: string,
  labelSubstring: string,
): Promise<void> {
  const options = await openSelectOptions(fixture, controlName);
  const option = options.find((candidate) =>
    (candidate.textContent ?? '').includes(labelSubstring),
  );
  if (!option) {
    throw new Error(`No option matching "${labelSubstring}" in ${controlName}.`);
  }
  option.click();
  fixture.detectChanges();
  await fixture.whenStable();
}

async function openOnlySelectOptions(fixture: ComponentFixture<unknown>): Promise<HTMLElement[]> {
  const trigger = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
    'mat-select .mat-mdc-select-trigger',
  );
  if (!trigger) {
    throw new Error('No select rendered.');
  }
  trigger.click();
  fixture.detectChanges();
  await fixture.whenStable();
  return Array.from(
    TestBed.inject(OverlayContainer)
      .getContainerElement()
      .querySelectorAll<HTMLElement>('mat-option'),
  );
}

export async function onlySelectOptionLabels(
  fixture: ComponentFixture<unknown>,
): Promise<string[]> {
  const options = await openOnlySelectOptions(fixture);
  return options.map((option) => (option.textContent ?? '').trim());
}

export async function onlySelectOptionGroups(
  fixture: ComponentFixture<unknown>,
): Promise<{ label: string; options: string[] }[]> {
  await openOnlySelectOptions(fixture);
  return Array.from(
    TestBed.inject(OverlayContainer).getContainerElement().querySelectorAll('mat-optgroup'),
    (group) => ({
      label: (
        group.querySelector('.mat-mdc-optgroup-label')?.textContent ??
        group.getAttribute('label') ??
        ''
      ).trim(),
      options: Array.from(group.querySelectorAll('mat-option'), (option) =>
        (option.textContent ?? '').trim(),
      ),
    }),
  );
}

export async function pickOnlySelectOption(
  fixture: ComponentFixture<unknown>,
  labelSubstring: string,
): Promise<void> {
  const options = await openOnlySelectOptions(fixture);
  const option = options.find((candidate) =>
    (candidate.textContent ?? '').includes(labelSubstring),
  );
  if (!option) {
    throw new Error(`No option matching "${labelSubstring}".`);
  }
  option.click();
  fixture.detectChanges();
  await fixture.whenStable();
}

export function onlySelectTriggerText(fixture: ComponentFixture<unknown>): string {
  return (
    (fixture.nativeElement as HTMLElement)
      .querySelector('mat-select .mat-mdc-select-trigger')
      ?.textContent?.trim() ?? ''
  );
}

export function selectTriggerState(
  fixture: ComponentFixture<unknown>,
  controlName: string,
): { triggerText: string; empty: boolean } {
  const select = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
    `mat-select[formcontrolname="${controlName}"], [formcontrolname="${controlName}"] mat-select`,
  );
  if (!select) {
    throw new Error(`No ${controlName} select rendered.`);
  }
  const trigger = select.querySelector<HTMLElement>('.mat-mdc-select-trigger');
  return {
    triggerText: (trigger?.textContent ?? '').trim(),
    empty: select.classList.contains('mat-mdc-select-empty'),
  };
}
