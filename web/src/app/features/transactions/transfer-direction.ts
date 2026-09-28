import type { TransactionTransferResponse, TransferDirection } from '../../api/portfolio';

// Backend enum Skarbiec.Portfolio.Features.Transfers.TransferDirection, serialized as its int in C#
// declaration order (asset-transfers-deposit-funding).
export const TRANSFER_DIRECTION = {
  Out: 0,
  In: 1,
} as const satisfies Record<string, TransferDirection>;

// The label a transfer leg carries beside its type: "Transfer to <asset> (<portfolio>)" on the Out
// leg, "Transfer from …" on the In leg — a translation key plus its parameters, rendered through
// the `transloco` pipe.
export function transferLabel(transfer: TransactionTransferResponse): {
  key: string;
  params: { asset: string; portfolio: string };
} {
  return {
    key:
      Number(transfer.direction) === TRANSFER_DIRECTION.Out
        ? 'enums.transferDirection.out'
        : 'enums.transferDirection.in',
    params: {
      asset: transfer.counterpartAssetName,
      portfolio: transfer.counterpartPortfolioName,
    },
  };
}
