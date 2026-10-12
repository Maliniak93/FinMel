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
const DIVIDEND = 4;
const INTEREST = 5;

export const TRANSACTION_TYPE_BUY: TransactionType = BUY;
export const TRANSACTION_TYPE_SELL: TransactionType = SELL;
export const TRANSACTION_TYPE_DEPOSIT: TransactionType = 2;
export const TRANSACTION_TYPE_WITHDRAW: TransactionType = WITHDRAW;
export const TRANSACTION_TYPE_DIVIDEND: TransactionType = DIVIDEND;
export const TRANSACTION_TYPE_INTEREST: TransactionType = INTEREST;

const CASH_LIKE_CLASSES: readonly number[] = [
  ASSET_CLASS.Cash,
  ASSET_CLASS.Deposit,
  ASSET_CLASS.Savings,
  ASSET_CLASS.Bond,
];
const CASH_LIKE_TYPES: readonly number[] = [TRANSACTION_TYPE_DEPOSIT, WITHDRAW];
const METAL_TYPES: readonly number[] = [BUY, SELL];
const SECURITY_TYPES: readonly number[] = [BUY, SELL, DIVIDEND, INTEREST];

function isPreciousMetal(assetClass: AssetClass | undefined): boolean {
  return assetClass !== undefined && Number(assetClass) === ASSET_CLASS.PreciousMetal;
}

export function isSecurityAssetClass(assetClass: AssetClass | undefined): boolean {
  return (
    assetClass !== undefined &&
    (Number(assetClass) === ASSET_CLASS.Stock || Number(assetClass) === ASSET_CLASS.Etf)
  );
}

function isSecurityIncome(value: TransactionType, assetClass: AssetClass | undefined): boolean {
  const type = Number(value);
  return isSecurityAssetClass(assetClass) && (type === DIVIDEND || type === INTEREST);
}

export function allowedTransactionTypes(
  assetClass: AssetClass,
): readonly { value: TransactionType; label: string }[] {
  if (isPreciousMetal(assetClass)) {
    return TRANSACTION_TYPES.filter((t) => METAL_TYPES.includes(t.value));
  }
  if (CASH_LIKE_CLASSES.includes(Number(assetClass))) {
    return TRANSACTION_TYPES.filter((t) => CASH_LIKE_TYPES.includes(t.value));
  }
  return TRANSACTION_TYPES.filter((t) => SECURITY_TYPES.includes(t.value));
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

// A precious metal's quantity is in pieces, priced per piece.
export function quantityFieldLabel(value: TransactionType, assetClass?: AssetClass): string {
  if (!isPricedTransactionType(value)) {
    return isSecurityIncome(value, assetClass)
      ? 'enums.quantityField.netAmountPln'
      : 'enums.quantityField.amount';
  }
  return isPreciousMetal(assetClass)
    ? 'enums.quantityField.pieces'
    : 'enums.quantityField.quantity';
}

export function unitPriceFieldLabel(assetClass?: AssetClass): string {
  return isPreciousMetal(assetClass) ? 'enums.quantityField.pricePerPiece' : 'common.unitPrice';
}
