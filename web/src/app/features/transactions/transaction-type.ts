import type { AssetClass, TransactionType } from '../../api/portfolio';
import { ASSET_CLASS } from '../assets/asset-class';

// Mirrors Skarbiec.Contracts.TransactionType in declaration order: the enum travels as its int.
export const TRANSACTION_TYPES: readonly { value: TransactionType; label: string }[] = [
  { value: 0, label: 'enums.transactionType.buy' },
  { value: 1, label: 'enums.transactionType.sell' },
  { value: 2, label: 'enums.transactionType.deposit' },
  { value: 3, label: 'enums.transactionType.withdraw' },
  { value: 4, label: 'enums.transactionType.dividend' },
  { value: 5, label: 'enums.transactionType.interest' },
];

const BUY = 0;
const SELL = 1;
const WITHDRAW = 3;

export const TRANSACTION_TYPE_BUY: TransactionType = BUY;
export const TRANSACTION_TYPE_DEPOSIT: TransactionType = 2;

const CASH_LIKE_CLASSES: readonly number[] = [
  ASSET_CLASS.Cash,
  ASSET_CLASS.Deposit,
  ASSET_CLASS.Savings,
];
const CASH_LIKE_TYPES: readonly number[] = [TRANSACTION_TYPE_DEPOSIT, WITHDRAW];

export function allowedTransactionTypes(
  assetClass: AssetClass,
): readonly { value: TransactionType; label: string }[] {
  return CASH_LIKE_CLASSES.includes(Number(assetClass))
    ? TRANSACTION_TYPES.filter((t) => CASH_LIKE_TYPES.includes(t.value))
    : TRANSACTION_TYPES;
}

export function transactionTypeLabel(value: TransactionType): string {
  return (
    TRANSACTION_TYPES.find((t) => t.value === Number(value))?.label ??
    'enums.transactionType.unknown'
  );
}

export function isPricedTransactionType(value: TransactionType): boolean {
  const type = Number(value);
  return type === BUY || type === SELL;
}

export function quantityFieldLabel(value: TransactionType): string {
  return isPricedTransactionType(value)
    ? 'enums.quantityField.quantity'
    : 'enums.quantityField.amount';
}
