import type { TransactionTransferResponse, TransferDirection } from '../../api/portfolio';

// Mirrors Skarbiec.Portfolio.Features.Transfers.TransferDirection in declaration order: the enum travels as its int.
export const TRANSFER_DIRECTION = {
  Out: 0,
  In: 1,
} as const satisfies Record<string, TransferDirection>;

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
