import { InjectionToken } from '@angular/core';
import type { FormGroup } from '@angular/forms';

import type { AddAssetRequest, UpdateAssetRequest } from '../../../api/portfolio';
import {
  buildInitialTransaction,
  type FirstTransactionGroup,
} from './blocks/first-transaction-fields/first-transaction-fields';

export type AssetFormBody = UpdateAssetRequest & Pick<AddAssetRequest, 'initialTransaction'>;

export interface AssetForm {
  readonly form: FormGroup;
  toBody(): AssetFormBody;
  submitBlockedReason?(): string | null;
}

export const ASSET_FORM = new InjectionToken<AssetForm>('AssetForm');

export const INITIAL_TRANSACTION_KEY = 'initialTransaction';

export function assetFormBody(
  fields: UpdateAssetRequest,
  initialTransaction: FirstTransactionGroup,
  isEdit: boolean,
): AssetFormBody {
  return isEdit
    ? fields
    : { ...fields, initialTransaction: buildInitialTransaction(initialTransaction) };
}
