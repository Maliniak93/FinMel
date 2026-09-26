import type { DepositResponse, PortfolioResponse } from '../../../api/portfolio';

// Shared arrange data for the deposit specs (form dialog, Deposits page, and the asset list /
// type-picker specs that hand off to the deposit form). Test-only: nothing in the app imports this
// file. Kept free of Vitest globals so it also type-checks under tsconfig.app.json.

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

// Backend enums arrive as ints (Skarbiec.Portfolio.Data.DepositTermUnit / DepositCapitalization,
// Features.Deposits.DepositStatus), in C# declaration order.
export const TERM_UNIT = { Days: 0, Months: 1 } as const;
export const CAPITALIZATION = { AtMaturity: 0, Monthly: 1, Quarterly: 2, Yearly: 3 } as const;
export const DEPOSIT_STATUS = { Active: 0, Due: 1, Settled: 2 } as const;

// Builds a DepositResponse from the spec's AC-1 deposit (10 000.00 PLN at 6 % from 2026-01-15 for
// 3 months, capitalised at maturity, taxed → net 119.83, final 10 119.83), overriding what a fact
// is about.
export function depositResponse(overrides: Partial<DepositResponse> = {}): DepositResponse {
  return {
    assetId: '11111111-1111-1111-1111-111111111111',
    portfolioId: savingsPortfolioId,
    portfolioName: 'Savings',
    portfolioIsArchived: false,
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
    ...overrides,
  } as DepositResponse;
}

// An Active deposit (matures next year) and a Due one (matured), in two different portfolios.
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

// term-deposits-settlement: a deposit settled on 2026-04-17 with what the bank actually paid (gross
// 150.00, tax 28.50 → net 121.50), which differs from its projection (net 119.83) — so a spec can
// tell the settled final amount (10 121.50) from the projected one (10 119.83).
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

// asset-transfers-deposit-funding: one row of GET /api/portfolio/transfer-candidates — a Cash asset
// the deposit form offers as its source of funds. Declared here (not imported from the generated
// client) so the fixtures do not depend on when `gen:api` picks the endpoint up.
export interface TransferCandidateFixture {
  assetId: string;
  name: string;
  portfolioId: string;
  portfolioName: string;
  balance: number;
}

export const walletPortfolioId = '99999999-9999-9999-9999-999999999999';

// A PLN Cash account holding 5 000 and a EUR one holding 2 500, both in a "Wallet" portfolio.
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

// The candidates per currency the fetch stubs answer with (a currency not listed has none).
export const transferCandidatesByCurrency: Record<string, TransferCandidateFixture[]> = {
  PLN: [plnCashCandidate],
  EUR: [eurCashCandidate],
};

// The settlement preview of `dueDeposit` (GET .../deposits/{assetId}/settlement-preview): the part-1
// projection, settled on the maturity date.
export const dueDepositSettlementPreview = {
  settledOn: '2026-04-15',
  grossInterest: 147.95,
  tax: 28.12,
  netInterest: 119.83,
  finalAmount: 10119.83,
};
