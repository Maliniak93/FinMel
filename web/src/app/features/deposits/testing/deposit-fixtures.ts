import { OverlayContainer } from '@angular/cdk/overlay';
import { TestBed, type ComponentFixture } from '@angular/core/testing';

import type { AccountResponse, DepositResponse } from '../../../api/portfolio';
import { bankAccountResponse } from '../../../../testing/bank-account-fixtures';

export const TERM_UNIT = { Days: 0, Months: 1 } as const;
export const CAPITALIZATION = { AtMaturity: 0, Monthly: 1, Quarterly: 2, Yearly: 3 } as const;
export const DEPOSIT_STATUS = { Active: 0, Due: 1, Settled: 2, PaidOut: 3 } as const;

export const mBankAccount: AccountResponse = bankAccountResponse({
  id: 'a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1',
  name: 'mBank',
  cashAssetId: 'a1a1a1a1-0000-0000-0000-000000000001',
  cashBalance: 20000,
  bank: { variant: 0, bankName: 'mBank', annualInterestRatePercent: 0 },
});

export const nestAccount: AccountResponse = bankAccountResponse({
  id: 'b2b2b2b2-b2b2-b2b2-b2b2-b2b2b2b2b2b2',
  name: 'Nest Oszczędnościowe',
  cashAssetId: 'b2b2b2b2-0000-0000-0000-000000000002',
  cashBalance: 0,
});

export const mBankUsdAccount: AccountResponse = bankAccountResponse({
  id: 'c3c3c3c3-c3c3-c3c3-c3c3-c3c3c3c3c3c3',
  name: 'mBank USD',
  currency: 'USD',
  cashAssetId: 'c3c3c3c3-0000-0000-0000-000000000003',
  cashBalance: 1000,
  bank: { variant: 0, bankName: 'mBank', annualInterestRatePercent: 0 },
});

export const archivedBankAccount: AccountResponse = bankAccountResponse({
  id: 'd4d4d4d4-d4d4-d4d4-d4d4-d4d4d4d4d4d4',
  name: 'Old savings',
  isArchived: true,
  cashAssetId: 'd4d4d4d4-0000-0000-0000-000000000004',
  cashBalance: 0,
});

export const bankAccountsForDeposits: AccountResponse[] = [
  mBankAccount,
  nestAccount,
  mBankUsdAccount,
  archivedBankAccount,
];

export function depositResponse(overrides: Partial<DepositResponse> = {}): DepositResponse {
  return {
    assetId: '11111111-1111-1111-1111-111111111111',
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
    sourceAccountId: mBankAccount.id,
    sourceAccountName: mBankAccount.name,
    returnAccountId: mBankAccount.id,
    returnAccountName: mBankAccount.name,
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
  returnAccountId: nestAccount.id,
  returnAccountName: nestAccount.name,
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
  returnAccountId: nestAccount.id,
  returnAccountName: nestAccount.name,
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
  paidOutToAccountName: paidOutDepositDestinationName,
} as Partial<DepositResponse>);

export const paidOutDepositWithoutAccountName: DepositResponse = depositResponse({
  assetId: 'dddddddd-dddd-dddd-dddd-dddddddddddd',
  name: 'Payout without a name',
  bankName: 'Bank F',
  status: DEPOSIT_STATUS.PaidOut,
  settledOn: '2026-04-17',
  settledGrossInterest: 150,
  settledTax: 28.5,
  paidOutOn: '2026-04-20',
  paidOutToAccountName: null,
} as Partial<DepositResponse>);

export const paidOutIntoBankDeposit: DepositResponse = depositResponse({
  assetId: 'c1c1c1c1-c1c1-c1c1-c1c1-c1c1c1c1c1c1',
  name: 'Deposit paid into a bank account',
  bankName: 'Bank J',
  status: DEPOSIT_STATUS.PaidOut,
  settledOn: '2026-04-17',
  settledGrossInterest: 150,
  settledTax: 28.5,
  paidOutOn: '2026-04-20',
  paidOutToAccountName: 'Nest Oszczędnościowe',
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
