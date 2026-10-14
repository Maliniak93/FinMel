import type { AccountResponse } from '../app/api/portfolio';

export const BANK_VARIANT = { Current: 0, Savings: 1 } as const;

type BankTerms = NonNullable<AccountResponse['bank']>;

export function bankTerms(overrides: Partial<BankTerms> = {}): BankTerms {
  return {
    variant: BANK_VARIANT.Savings,
    bankName: 'Nest Bank',
    annualInterestRatePercent: 5,
    interestDue: false,
    duePeriodCount: 0,
    lastSettlement: null,
    interestCreditedNet: 0,
    ...overrides,
  };
}

export function bankAccountResponse(
  overrides: Partial<Omit<AccountResponse, 'bank'>> & { bank?: Partial<BankTerms> } = {},
): AccountResponse {
  const { bank, ...rest } = overrides;
  return {
    id: '55555555-5555-5555-5555-555555555555',
    name: 'Nest Oszczędnościowe',
    type: 2,
    wrapper: 0,
    currency: 'PLN',
    isArchived: false,
    cashAssetId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1',
    cashBalance: 10000,
    bank: bankTerms(bank),
    ...rest,
  };
}

export const interestPreview = {
  periodStart: '2026-08-01',
  periodEnd: '2026-08-31',
  annualInterestRatePercent: 5,
  averageDailyBalance: 10000,
  grossInterest: 42.47,
  tax: 8.07,
  netInterest: 34.4,
  duePeriodCount: 1,
};
