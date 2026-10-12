import type { AccountType, TaxWrapper } from '../../api/portfolio';

// Mirrors Skarbiec.Portfolio.Data.AccountType and TaxWrapper: the enums travel as their ints.
export const ACCOUNT_TYPE = { Brokerage: 0, Bonds: 1 } as const satisfies Record<
  string,
  AccountType
>;

export const TAX_WRAPPER = {
  Regular: 0,
  Ike: 1,
  Ikze: 2,
} as const satisfies Record<string, TaxWrapper>;

export const ACCOUNT_TYPES: readonly { value: AccountType; label: string }[] = [
  { value: ACCOUNT_TYPE.Brokerage, label: 'enums.accountType.brokerage' },
  { value: ACCOUNT_TYPE.Bonds, label: 'enums.accountType.bonds' },
];

export const TAX_WRAPPERS: readonly { value: TaxWrapper; label: string }[] = [
  { value: TAX_WRAPPER.Regular, label: 'enums.taxWrapper.regular' },
  { value: TAX_WRAPPER.Ike, label: 'enums.taxWrapper.ike' },
  { value: TAX_WRAPPER.Ikze, label: 'enums.taxWrapper.ikze' },
];

export function accountTypeLabel(value: AccountType): string {
  return ACCOUNT_TYPES.find((t) => t.value === Number(value))?.label ?? 'enums.accountType.unknown';
}

export function taxWrapperLabel(value: TaxWrapper): string {
  return TAX_WRAPPERS.find((w) => w.value === Number(value))?.label ?? 'enums.taxWrapper.unknown';
}
