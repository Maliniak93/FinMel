import type { AssetClass, TransactionType } from '../../api/portfolio';
import { ASSET_CLASS } from '../assets/asset-class';

// Backend enum (Skarbiec.Contracts.TransactionType) serializes as its underlying int, so the
// generated client types it as a bare `number` — labels for the UI have to be maintained here, in
// the same declaration order as the C# enum. Mirrors asset-class.ts's precedent (T1.11).
export const TRANSACTION_TYPES: readonly { value: TransactionType; label: string }[] = [
  { value: 0, label: 'Buy' },
  { value: 1, label: 'Sell' },
  { value: 2, label: 'Deposit' },
  { value: 3, label: 'Withdraw' },
  { value: 4, label: 'Dividend' },
  { value: 5, label: 'Interest' },
];

const BUY = 0;
const SELL = 1;
const WITHDRAW = 3;

// Exported for callers that need to set/compare a specific transaction type by name rather than by
// index into TRANSACTION_TYPES (first-transaction-fields' pre-fill, T1.11).
export const TRANSACTION_TYPE_BUY: TransactionType = BUY;
export const TRANSACTION_TYPE_DEPOSIT: TransactionType = 2;

// cash-transaction-types: the mirror of Portfolio's AssetTransactionTypes rule (the API is the source
// of truth and answers 400 to anything else) — a cash-like class (Cash, Deposit) accepts only
// Deposit/Withdraw, every other class all six types, in TRANSACTION_TYPES order.
const CASH_LIKE_CLASSES: readonly number[] = [ASSET_CLASS.Cash, ASSET_CLASS.Deposit];
const CASH_LIKE_TYPES: readonly number[] = [TRANSACTION_TYPE_DEPOSIT, WITHDRAW];

export function allowedTransactionTypes(
  assetClass: AssetClass,
): readonly { value: TransactionType; label: string }[] {
  return CASH_LIKE_CLASSES.includes(Number(assetClass))
    ? TRANSACTION_TYPES.filter((t) => CASH_LIKE_TYPES.includes(t.value))
    : TRANSACTION_TYPES;
}

export function transactionTypeLabel(value: TransactionType): string {
  return TRANSACTION_TYPES.find((t) => t.value === Number(value))?.label ?? 'Unknown';
}

// Only Buy/Sell are priced trades against a unit price (TransactionQuantityCalculator treats every
// other type as a plain quantity delta or value-only movement) — the form hides "unit price" for
// the rest and submits 1 for it, so the server's `quantity × unitPrice` PLN value stays one formula
// across every row instead of branching per type.
export function isPricedTransactionType(value: TransactionType): boolean {
  const type = Number(value);
  return type === BUY || type === SELL;
}

export function quantityFieldLabel(value: TransactionType): string {
  return isPricedTransactionType(value) ? 'Quantity' : 'Amount';
}
