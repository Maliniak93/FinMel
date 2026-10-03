import type { TreasuryBondType } from '../../api/marketdata';

// Mirrors TreasuryBondType in declaration order: the enum travels as an int.
export const TREASURY_BOND_TYPE = {
  Ots: 0,
  Ror: 1,
  Dor: 2,
  Tos: 3,
  Coi: 4,
  Edo: 5,
  Ros: 6,
  Rod: 7,
} as const satisfies Record<string, TreasuryBondType>;

export type BondInterestPayout = 'coupon' | 'capitalised';

export type BondFollowingRate = 'fixed' | 'nbpReference' | 'inflation';

export interface BondTypeInfo {
  readonly type: TreasuryBondType;
  readonly label: string;
  readonly termLabel: string;
  readonly termMonths: number;
  readonly periodMonths: number;
  readonly payout: BondInterestPayout;
  readonly followingRate: BondFollowingRate;
  // PLN per bond: the current default, which the user may override on a holding.
  readonly defaultEarlyRedemptionFee: number;
}

export const BOND_TYPES: readonly BondTypeInfo[] = [
  {
    type: TREASURY_BOND_TYPE.Ots,
    label: 'enums.treasuryBondType.ots',
    termLabel: 'bonds.term.ots',
    termMonths: 3,
    periodMonths: 3,
    payout: 'capitalised',
    followingRate: 'fixed',
    defaultEarlyRedemptionFee: 0,
  },
  {
    type: TREASURY_BOND_TYPE.Ror,
    label: 'enums.treasuryBondType.ror',
    termLabel: 'bonds.term.ror',
    termMonths: 12,
    periodMonths: 1,
    payout: 'coupon',
    followingRate: 'nbpReference',
    defaultEarlyRedemptionFee: 0.5,
  },
  {
    type: TREASURY_BOND_TYPE.Dor,
    label: 'enums.treasuryBondType.dor',
    termLabel: 'bonds.term.dor',
    termMonths: 24,
    periodMonths: 1,
    payout: 'coupon',
    followingRate: 'nbpReference',
    defaultEarlyRedemptionFee: 0.7,
  },
  {
    type: TREASURY_BOND_TYPE.Tos,
    label: 'enums.treasuryBondType.tos',
    termLabel: 'bonds.term.tos',
    termMonths: 36,
    periodMonths: 12,
    payout: 'capitalised',
    followingRate: 'fixed',
    defaultEarlyRedemptionFee: 1,
  },
  {
    type: TREASURY_BOND_TYPE.Coi,
    label: 'enums.treasuryBondType.coi',
    termLabel: 'bonds.term.coi',
    termMonths: 48,
    periodMonths: 12,
    payout: 'coupon',
    followingRate: 'inflation',
    defaultEarlyRedemptionFee: 2,
  },
  {
    type: TREASURY_BOND_TYPE.Edo,
    label: 'enums.treasuryBondType.edo',
    termLabel: 'bonds.term.edo',
    termMonths: 120,
    periodMonths: 12,
    payout: 'capitalised',
    followingRate: 'inflation',
    defaultEarlyRedemptionFee: 3,
  },
  {
    type: TREASURY_BOND_TYPE.Ros,
    label: 'enums.treasuryBondType.ros',
    termLabel: 'bonds.term.ros',
    termMonths: 72,
    periodMonths: 12,
    payout: 'capitalised',
    followingRate: 'inflation',
    defaultEarlyRedemptionFee: 2,
  },
  {
    type: TREASURY_BOND_TYPE.Rod,
    label: 'enums.treasuryBondType.rod',
    termLabel: 'bonds.term.rod',
    termMonths: 144,
    periodMonths: 12,
    payout: 'capitalised',
    followingRate: 'inflation',
    defaultEarlyRedemptionFee: 3,
  },
];

export function bondTypeInfo(type: TreasuryBondType): BondTypeInfo | undefined {
  return BOND_TYPES.find((info) => info.type === Number(type));
}
