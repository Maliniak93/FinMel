import type { TransactionTransferResponse, TransferDirection } from '../../api/portfolio';

// Mirrors Skarbiec.Portfolio.Features.Transfers.TransferDirection in declaration order: the enum travels as its int.
export const TRANSFER_DIRECTION = {
  Out: 0,
  In: 1,
} as const satisfies Record<string, TransferDirection>;

export function transferLabel(transfer: TransactionTransferResponse): {
  key: string;
  params: { asset: string; account: string };
} {
  const direction = Number(transfer.direction) === TRANSFER_DIRECTION.Out ? 'out' : 'in';
  const account = transfer.counterpartAccountName ?? '';
  return {
    key: account
      ? `enums.transferDirection.${direction}WithAccount`
      : `enums.transferDirection.${direction}`,
    params: { asset: transfer.counterpartAssetName, account },
  };
}
