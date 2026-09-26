import type { TransactionTransferResponse, TransferDirection } from '../../api/portfolio';

// Backend enum Skarbiec.Portfolio.Features.Transfers.TransferDirection, serialized as its int in C#
// declaration order (asset-transfers-deposit-funding).
export const TRANSFER_DIRECTION = {
  Out: 0,
  In: 1,
} as const satisfies Record<string, TransferDirection>;

// The label a transfer leg carries beside its type: "Transfer to <asset> (<portfolio>)" on the Out
// leg, "Transfer from …" on the In leg.
export function transferLabel(transfer: TransactionTransferResponse): string {
  const direction = Number(transfer.direction) === TRANSFER_DIRECTION.Out ? 'to' : 'from';
  return `Transfer ${direction} ${transfer.counterpartAssetName} (${transfer.counterpartPortfolioName})`;
}
