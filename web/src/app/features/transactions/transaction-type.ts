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
];

const BUY = 0;
const SELL = 1;

// Exported for callers that need to set/compare a specific transaction type by name rather than by
// index into TRANSACTION_TYPES (first-transaction-fields' pre-fill, T1.11).
export const TRANSACTION_TYPE_BUY: TransactionType = BUY;
export const TRANSACTION_TYPE_DEPOSIT: TransactionType = 2;

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
