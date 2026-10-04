export const BOND_STATUS = { Active: 0, InterestDue: 1, Matured: 2 } as const;
export const BOND_PERIOD_STATE = { Upcoming: 0, Due: 1, Settled: 2 } as const;

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
  duePeriodCount: number;
  lastSettlement: BondSettlementFixture | null;
  periods: BondPeriodFixture[];
}

export interface BondSettlementFixture {
  settlementId: string;
  ratePercent: number;
  bondCount: number;
  grossInterest: number;
  tax: number;
}

export interface BondPeriodFixture {
  index: number;
  start: string;
  end: string;
  state: number;
  settlement: BondSettlementFixture | null;
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
    duePeriodCount: 0,
    lastSettlement: null,
    periods: [
      {
        index: 1,
        start: '2026-10-01',
        end: '2027-10-01',
        state: BOND_PERIOD_STATE.Upcoming,
        settlement: null,
      },
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

export const fundingCashId = 'c0c0c0c0-1111-1111-1111-111111111111';
export const otherCashId = 'c0c0c0c0-2222-2222-2222-222222222222';

function rorPeriod(
  index: number,
  start: string,
  end: string,
  state: number,
  settlement: BondSettlementFixture | null = null,
): BondPeriodFixture {
  return { index, start, end, state, settlement };
}

export const dueRorBond = bondResponse({
  assetId: 'a1a1a1a1-0000-0000-0000-00000000a001',
  name: 'Ror due purchase',
  seriesCode: 'ROR0627',
  type: 1,
  purchaseDate: '2026-06-10',
  firstPeriodRatePercent: 4,
  marginPercent: null,
  maturityDate: '2027-06-10',
  fundingAssetId: fundingCashId,
  fundingAssetName: 'Wallet cash',
  status: BOND_STATUS.InterestDue,
  duePeriodCount: 3,
  periods: [
    rorPeriod(1, '2026-06-10', '2026-07-10', BOND_PERIOD_STATE.Due),
    rorPeriod(2, '2026-07-10', '2026-08-10', BOND_PERIOD_STATE.Due),
    rorPeriod(3, '2026-08-10', '2026-09-10', BOND_PERIOD_STATE.Due),
    rorPeriod(4, '2026-09-10', '2026-10-10', BOND_PERIOD_STATE.Upcoming),
  ],
});

export const dueEdoBond = bondResponse({
  assetId: 'a1a1a1a1-0000-0000-0000-00000000a002',
  name: 'Edo due purchase',
  purchaseDate: '2025-10-01',
  status: BOND_STATUS.InterestDue,
  duePeriodCount: 1,
  periods: [
    rorPeriod(1, '2025-10-01', '2026-10-01', BOND_PERIOD_STATE.Due),
    rorPeriod(2, '2026-10-01', '2027-10-01', BOND_PERIOD_STATE.Upcoming),
  ],
});

const firstRorSettlement: BondSettlementFixture = {
  settlementId: 's1s1s1s1-0000-0000-0000-000000000001',
  ratePercent: 4,
  bondCount: 50,
  grossInterest: 16.5,
  tax: 3.14,
};

const secondRorSettlement: BondSettlementFixture = {
  settlementId: 's1s1s1s1-0000-0000-0000-000000000002',
  ratePercent: 3.75,
  bondCount: 50,
  grossInterest: 15.5,
  tax: 2.95,
};

export const settledRorBond = bondResponse({
  assetId: 'a1a1a1a1-0000-0000-0000-00000000a003',
  name: 'Ror settled purchase',
  seriesCode: 'ROR0627',
  type: 1,
  purchaseDate: '2026-06-10',
  firstPeriodRatePercent: 4,
  marginPercent: null,
  maturityDate: '2027-06-10',
  fundingAssetId: fundingCashId,
  fundingAssetName: 'Wallet cash',
  status: BOND_STATUS.Active,
  duePeriodCount: 0,
  lastSettlement: secondRorSettlement,
  periods: [
    rorPeriod(1, '2026-06-10', '2026-07-10', BOND_PERIOD_STATE.Settled, firstRorSettlement),
    rorPeriod(2, '2026-07-10', '2026-08-10', BOND_PERIOD_STATE.Settled, secondRorSettlement),
    rorPeriod(3, '2026-08-10', '2026-09-10', BOND_PERIOD_STATE.Upcoming),
  ],
});

export const rorSeries = {
  code: 'ROR0627',
  type: 1,
  isin: 'PL0000000010',
  saleStart: '2026-06-01',
  saleEnd: '2026-06-30',
  issuePrice: 100,
  swapPrice: 99.9,
  marginPercent: 0,
  periodRates: [{ periodIndex: 2, ratePercent: 3.75 }],
};

export const edoSeries = {
  code: 'EDO1036',
  type: 5,
  isin: 'PL0000000003',
  saleStart: '2026-10-01',
  saleEnd: '2026-10-31',
  issuePrice: 100,
  swapPrice: 99.9,
  marginPercent: 2,
  periodRates: [{ periodIndex: 2, ratePercent: 6.1 }],
};

export const rorPreviewResponse = {
  rows: [
    {
      periodIndex: 1,
      start: '2026-06-10',
      end: '2026-07-10',
      ratePercent: 4,
      bondCount: 50,
      gross: 16.5,
      tax: 3.14,
      net: 13.36,
    },
    {
      periodIndex: 2,
      start: '2026-07-10',
      end: '2026-08-10',
      ratePercent: 3.75,
      bondCount: 50,
      gross: 15.5,
      tax: 2.95,
      net: 12.55,
    },
  ],
  totals: { gross: 32, tax: 6.09, net: 25.91 },
};

export const cashCandidates = [
  {
    assetId: fundingCashId,
    name: 'Wallet cash',
    portfolioId: '22222222-2222-2222-2222-222222222221',
    portfolioName: 'Wallet',
    balance: 1000,
  },
  {
    assetId: otherCashId,
    name: 'Other cash',
    portfolioId: '22222222-2222-2222-2222-222222222221',
    portfolioName: 'Wallet',
    balance: 50,
  },
];
