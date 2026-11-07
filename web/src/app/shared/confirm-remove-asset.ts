import type { MatDialog } from '@angular/material/dialog';
import type { MatSnackBar } from '@angular/material/snack-bar';
import { translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import { deleteApiPortfolioAssetsById } from '../api/portfolio';
import { readProblemDetails } from '../core/auth/problem-details';
import { activeLocale } from '../core/i18n/language';
import { ConfirmDialog } from './confirm-dialog/confirm-dialog';
import { formatQuantity } from './format';

export interface RemovableAsset {
  assetId: string;
  name: string;
  transactionCount: number;
  held: { quantity: number; unit: 'grams' | 'pieces' } | null;
}

export async function confirmRemoveAsset(
  dialog: MatDialog,
  snackBar: MatSnackBar,
  asset: RemovableAsset,
): Promise<boolean> {
  const count = asset.transactionCount;
  const messageKey =
    count > 0
      ? `assetArchive.remove.message.${new Intl.PluralRules(activeLocale()).select(count)}`
      : 'assetArchive.remove.message.none';
  let message = translate(messageKey, { name: asset.name, count });
  if (asset.held) {
    const amount = translate(`assetArchive.remove.${asset.held.unit}`, {
      value: formatQuantity(asset.held.quantity),
    });
    message += ` ${translate('assetArchive.remove.held', { amount })}`;
  }

  const confirmed = await firstValueFrom(
    dialog
      .open(ConfirmDialog, {
        data: {
          title: translate('assetArchive.remove.title', { name: asset.name }),
          message,
          confirmLabel: translate('common.delete'),
          destructive: true,
        },
      })
      .afterClosed(),
  );
  if (!confirmed) {
    return false;
  }

  const result = await deleteApiPortfolioAssetsById({ path: { id: asset.assetId } });
  if (result.error) {
    snackBar.open(
      readProblemDetails(result.error).detail ?? translate('assetArchive.remove.failed'),
      translate('common.dismiss'),
    );
    return false;
  }

  return true;
}
