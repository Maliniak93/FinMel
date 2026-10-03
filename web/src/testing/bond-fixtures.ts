export const BOND_STATUS = { Active: 0, InterestDue: 1, Matured: 2 } as const;
export const BOND_PERIOD_STATE = { Upcoming: 0, Due: 1 } as const;

export const bondPortfolioId = '22222222-2222-2222-2222-222222222222';

export interface BondFixture {
  assetId: string;
  portfolioId: string;
  portfolioName: string;
  portfolioIsArchived: boolean;
  isArchived: boolean;
  name: string;
  seriesCode: string;
  type: number;
  purchaseDate: string;
  bondCount: number;
  purchasePricePerBond: number;
  firstPeriodRatePercent: number;
  marginPercent: number | null;
  earlyRedemptionFeePerBond: number;
  taxExempt: boolean;
  maturityDate: string;
  nominalValue: number;
  bookValue: number;
  fundingAssetId: string | null;
  fundingAssetName: string | null;
  status: number;
  periods: { index: number; start: string; end: string; state: number }[];
}

export function bondResponse(overrides: Partial<BondFixture> = {}): BondFixture {
  return {
    assetId: '66666666-6666-6666-6666-666666666666',
    portfolioId: bondPortfolioId,
    portfolioName: 'Bonds',
    portfolioIsArchived: false,
    isArchived: false,
    name: 'Edo purchase',
    seriesCode: 'EDO1036',
    type: 5,
    purchaseDate: '2026-10-01',
    bondCount: 50,
    purchasePricePerBond: 100,
    firstPeriodRatePercent: 5.35,
    marginPercent: 2,
    earlyRedemptionFeePerBond: 3,
    taxExempt: false,
    maturityDate: '2036-10-01',
    nominalValue: 5000,
    bookValue: 5000,
    fundingAssetId: null,
    fundingAssetName: null,
    status: BOND_STATUS.Active,
    periods: [
      { index: 1, start: '2026-10-01', end: '2027-10-01', state: BOND_PERIOD_STATE.Upcoming },
    ],
    ...overrides,
  };
}

export const activeBond = bondResponse();

export const interestDueBond = bondResponse({
  assetId: '77777777-7777-7777-7777-777777777777',
  name: 'Ror purchase',
  seriesCode: 'ROR0627',
  type: 1,
  purchaseDate: '2026-06-10',
  maturityDate: '2027-06-10',
  marginPercent: null,
  status: BOND_STATUS.InterestDue,
});

export const maturedBond = bondResponse({
  assetId: '88888888-8888-8888-8888-888888888888',
  name: 'Ots purchase',
  seriesCode: 'OTS0926',
  type: 0,
  purchaseDate: '2026-06-10',
  maturityDate: '2026-09-10',
  marginPercent: null,
  status: BOND_STATUS.Matured,
});

export const archivedBond = bondResponse({
  assetId: '99999999-9999-9999-9999-999999999990',
  name: 'Archived purchase',
  isArchived: true,
});

export interface BondSeriesFixture {
  code: string;
  type: number;
  isin: string;
  saleStart: string;
  saleEnd: string;
  issuePrice: number;
  swapPrice: number | null;
  marginPercent: number | null;
  firstPeriodRatePercent: number;
}

export const octoberOffer: BondSeriesFixture[] = [
  {
    code: 'ROR1027',
    type: 1,
    isin: 'PL0000000002',
    saleStart: '2026-10-01',
    saleEnd: '2026-10-31',
    issuePrice: 100,
    swapPrice: 99.9,
    marginPercent: 0,
    firstPeriodRatePercent: 4.25,
  },
  {
    code: 'EDO1036',
    type: 5,
    isin: 'PL0000000003',
    saleStart: '2026-10-01',
    saleEnd: '2026-10-31',
    issuePrice: 100,
    swapPrice: 99.9,
    marginPercent: 2,
    firstPeriodRatePercent: 5.35,
  },
];

export const septemberOffer: BondSeriesFixture[] = [
  {
    code: 'COI0930',
    type: 4,
    isin: 'PL0000000004',
    saleStart: '2026-09-01',
    saleEnd: '2026-09-30',
    issuePrice: 100,
    swapPrice: 99.9,
    marginPercent: 1.25,
    firstPeriodRatePercent: 5.6,
  },
];
