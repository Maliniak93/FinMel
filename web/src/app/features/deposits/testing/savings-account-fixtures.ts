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
    ...overrides,
  } as SavingsAccountResponse;
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
