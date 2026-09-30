import type { SavingsAccountResponse } from '../../../api/portfolio';
import { archivedPortfolioId, reservePortfolioId, savingsPortfolioId } from './deposit-fixtures';

// Shared arrange data for the savings-account specs (form dialog, the Savings accounts tab, the
// Deposits & savings page, and the asset list / type-picker specs that hand off to the savings
// form). Test-only: nothing in the app imports this file. Kept free of Vitest globals so it also
// type-checks under tsconfig.app.json.

// Builds a SavingsAccountResponse — a PLN account at 5.25 % holding 10 000 in the "Savings"
// portfolio — overriding what a fact is about.
export function savingsAccountResponse(
  overrides: Partial<SavingsAccountResponse> = {},
): SavingsAccountResponse {
  return {
    assetId: '11111111-aaaa-1111-aaaa-111111111111',
    portfolioId: savingsPortfolioId,
    portfolioName: 'Savings',
    portfolioIsArchived: false,
    isArchived: false,
    name: 'Savings account',
    bankName: 'Test bank',
    currency: 'PLN',
    balance: 10000,
    annualInterestRatePercent: 5.25,
    taxExempt: false,
    interestDue: false,
    duePeriodCount: 0,
    lastSettlement: null,
    ...overrides,
  };
}

export const activeSavingsAccount: SavingsAccountResponse = savingsAccountResponse({
  assetId: '22222222-aaaa-2222-aaaa-222222222222',
  portfolioId: reservePortfolioId,
  portfolioName: 'Reserve',
  name: 'Emergency fund',
  bankName: 'Bank A',
  balance: 25000,
  annualInterestRatePercent: 4.5,
});

// A tax-free (IKE/IKZE) account in a EUR currency, so a spec can tell the balance's own currency
// from PLN.
export const taxFreeEurSavingsAccount: SavingsAccountResponse = savingsAccountResponse({
  assetId: '33333333-aaaa-3333-aaaa-333333333333',
  name: 'IKE savings',
  bankName: 'Bank B',
  currency: 'EUR',
  balance: 1234.5,
  annualInterestRatePercent: 3,
  taxExempt: true,
});

// An account of an archived portfolio: read-only, its row offers no action.
export const archivedPortfolioSavingsAccount: SavingsAccountResponse = savingsAccountResponse({
  assetId: '44444444-aaaa-4444-aaaa-444444444444',
  portfolioId: archivedPortfolioId,
  portfolioName: 'Old savings',
  portfolioIsArchived: true,
  name: 'Frozen account',
  bankName: 'Bank C',
});

// asset-archive: an account archived on its own, in a live portfolio.
export const archivedSavingsAccount: SavingsAccountResponse = savingsAccountResponse({
  assetId: '55555555-aaaa-5555-aaaa-555555555555',
  name: 'Shelved account',
  bankName: 'Bank D',
  isArchived: true,
});

// savings-interest-settlement: an account with one ended, unsettled month — the row carries the
// "Settle interest" icon.
export const dueSavingsAccount: SavingsAccountResponse = savingsAccountResponse({
  assetId: '66666666-aaaa-6666-aaaa-666666666666',
  name: 'Interest due account',
  bankName: 'Bank E',
  interestDue: true,
  duePeriodCount: 1,
  lastSettlement: null,
});

// Three ended, unsettled months: the icon's tooltip states the count.
export const manyMonthsDueSavingsAccount: SavingsAccountResponse = savingsAccountResponse({
  assetId: '77777777-aaaa-7777-aaaa-777777777777',
  name: 'Three months due',
  bankName: 'Bank F',
  interestDue: true,
  duePeriodCount: 3,
  lastSettlement: null,
});

// Settled up to and including September 2026 (net 33.29), nothing due since: no settle icon, but the
// latest settlement can be undone.
export const settledSavingsAccount: SavingsAccountResponse = savingsAccountResponse({
  assetId: '88888888-aaaa-8888-aaaa-888888888888',
  name: 'Settled account',
  bankName: 'Bank G',
  balance: 10033.29,
  interestDue: false,
  duePeriodCount: 0,
  lastSettlement: {
    settlementId: 'abcdabcd-1111-2222-3333-444444444444',
    periodStart: '2026-09-01',
    periodEnd: '2026-09-30',
    grossInterest: 41.1,
    tax: 7.81,
    netInterest: 33.29,
  },
});

// Due and previously settled, in an archived portfolio: read-only, so its row offers neither the
// settle nor the undo icon.
export const archivedPortfolioDueSavingsAccount: SavingsAccountResponse = savingsAccountResponse({
  assetId: '99999999-aaaa-9999-aaaa-999999999999',
  portfolioId: archivedPortfolioId,
  portfolioName: 'Old savings',
  portfolioIsArchived: true,
  name: 'Frozen due account',
  bankName: 'Bank H',
  interestDue: true,
  duePeriodCount: 1,
  lastSettlement: {
    settlementId: 'abcdabcd-5555-6666-7777-888888888888',
    periodStart: '2026-08-01',
    periodEnd: '2026-08-31',
    grossInterest: 42.47,
    tax: 8.07,
    netInterest: 34.4,
  },
});

// GET .../savings-accounts/{assetId}/interest-preview for September 2026 of a 10 000 PLN account at
// 5 %: 41.10 gross, 7.81 tax, 33.29 net.
export const savingsInterestPreview = {
  periodStart: '2026-09-01',
  periodEnd: '2026-09-30',
  annualInterestRatePercent: 5,
  averageDailyBalance: 10000,
  grossInterest: 41.1,
  tax: 7.81,
  netInterest: 33.29,
  duePeriodCount: 1,
};
