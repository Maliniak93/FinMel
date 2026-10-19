import type { AccountType, BankVariant, TaxWrapper } from '../../api/portfolio';

// Mirrors Skarbiec.Portfolio.Data.AccountType and TaxWrapper: the enums travel as their ints.
export const ACCOUNT_TYPE = { Brokerage: 0, Bonds: 1, Bank: 2 } as const satisfies Record<
  string,
  AccountType
>;

// Mirrors Skarbiec.Portfolio.Data.BankVariant.
export const BANK_VARIANT = { Current: 0, Savings: 1 } as const satisfies Record<
  string,
  BankVariant
>;

export const TAX_WRAPPER = {
  Regular: 0,
  Ike: 1,
  Ikze: 2,
} as const satisfies Record<string, TaxWrapper>;

export const INVESTMENT_ACCOUNT_TYPES: readonly { value: AccountType; label: string }[] = [
  { value: ACCOUNT_TYPE.Brokerage, label: 'enums.accountType.brokerage' },
  { value: ACCOUNT_TYPE.Bonds, label: 'enums.accountType.bonds' },
];

export const BANK_ACCOUNT_TYPES: readonly { value: AccountType; label: string }[] = [
  { value: ACCOUNT_TYPE.Bank, label: 'enums.accountType.bank' },
];

export const ACCOUNT_TYPES: readonly { value: AccountType; label: string }[] = [
  ...INVESTMENT_ACCOUNT_TYPES,
  ...BANK_ACCOUNT_TYPES,
];

export const BANK_VARIANTS: readonly { value: BankVariant; label: string }[] = [
  { value: BANK_VARIANT.Current, label: 'enums.bankVariant.current' },
  { value: BANK_VARIANT.Savings, label: 'enums.bankVariant.savings' },
];

export const TAX_WRAPPERS: readonly { value: TaxWrapper; label: string }[] = [
  { value: TAX_WRAPPER.Regular, label: 'enums.taxWrapper.regular' },
  { value: TAX_WRAPPER.Ike, label: 'enums.taxWrapper.ike' },
  { value: TAX_WRAPPER.Ikze, label: 'enums.taxWrapper.ikze' },
];

export function accountTypeLabel(value: AccountType): string {
  return ACCOUNT_TYPES.find((t) => t.value === Number(value))?.label ?? 'enums.accountType.unknown';
}

export function bankVariantLabel(value: BankVariant): string {
  return BANK_VARIANTS.find((v) => v.value === Number(value))?.label ?? 'enums.bankVariant.unknown';
}

export function taxWrapperLabel(value: TaxWrapper): string {
  return TAX_WRAPPERS.find((w) => w.value === Number(value))?.label ?? 'enums.taxWrapper.unknown';
}
