import type { TransactionType } from '../../api/portfolio';

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
  { value: 6, label: 'Fee' },
];

const BUY = 0;
const SELL = 1;
const FEE = 6;

export function transactionTypeLabel(value: TransactionType): string {
  return TRANSACTION_TYPES.find((t) => t.value === Number(value))?.label ?? 'Unknown';
}

// Only Buy/Sell are priced trades against a unit price (TransactionQuantityCalculator treats every
// other type as a plain quantity delta or value-only movement) — the form hides "unit price" for
// the rest and submits 1 for it, so `quantity * unitPrice` stays one formula for the Value column
// across every row instead of branching per type.
export function isPricedTransactionType(value: TransactionType): boolean {
  const type = Number(value);
  return type === BUY || type === SELL;
}

// TransactionType.Fee already *is* the fee being recorded — a separate "fee" amount on the same
// row would double up on the same concept, so that field is hidden only for this one type.
export function showsFeeField(value: TransactionType): boolean {
  return Number(value) !== FEE;
}

export function quantityFieldLabel(value: TransactionType): string {
  return isPricedTransactionType(value) ? 'Quantity' : 'Amount';
}
