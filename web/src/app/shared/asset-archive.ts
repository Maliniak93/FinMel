import type { MatDialog } from '@angular/material/dialog';
import type { MatSnackBar } from '@angular/material/snack-bar';
import { translate } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';

import {
  postApiPortfolioPortfoliosByPortfolioIdAssetsByIdArchive,
  postApiPortfolioPortfoliosByPortfolioIdAssetsByIdRestore,
} from '../api/portfolio';
import { readProblemDetails } from '../core/auth/problem-details';
import { ConfirmDialog } from './confirm-dialog/confirm-dialog';

// The one asset a page archives or restores (asset-archive): an AssetResponse or a DepositResponse
// mapped to its ids and name.
export interface ArchivableAsset {
  portfolioId: string;
  assetId: string;
  name: string;
}

// asset-archive: confirms, then archives (`archive` true) or restores one asset. Resolves true once
// the server accepted it, so the page reloads its list; false when the user cancelled or the call
// failed (a snack bar says why). Shared by the asset list and the Deposits page, which offer the same
// Archive / Restore row menu items.
export async function confirmSetAssetArchived(
  dialog: MatDialog,
  snackBar: MatSnackBar,
  asset: ArchivableAsset,
  archive: boolean,
): Promise<boolean> {
  const data = archive
    ? {
        title: translate('assetArchive.archive.title', { name: asset.name }),
        message: translate('assetArchive.archive.message'),
        confirmLabel: translate('common.archive'),
      }
    : {
        title: translate('assetArchive.restore.title', { name: asset.name }),
        message: translate('assetArchive.restore.message'),
        confirmLabel: translate('common.restore'),
      };
  const confirmed = await firstValueFrom(dialog.open(ConfirmDialog, { data }).afterClosed());
  if (!confirmed) {
    return false;
  }

  const options = { path: { portfolioId: asset.portfolioId, id: asset.assetId } };
  const result = archive
    ? await postApiPortfolioPortfoliosByPortfolioIdAssetsByIdArchive(options)
    : await postApiPortfolioPortfoliosByPortfolioIdAssetsByIdRestore(options);
  if (result.error) {
    snackBar.open(
      readProblemDetails(result.error).detail ??
        translate(archive ? 'assetArchive.archive.failed' : 'assetArchive.restore.failed'),
      translate('common.dismiss'),
    );
    return false;
  }

  return true;
}
