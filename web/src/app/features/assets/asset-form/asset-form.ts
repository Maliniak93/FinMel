import { InjectionToken } from '@angular/core';
import type { FormGroup } from '@angular/forms';

import type { AddAssetRequest, UpdateAssetRequest } from '../../../api/portfolio';
import {
  buildInitialTransaction,
  type FirstTransactionGroup,
} from './blocks/first-transaction-fields/first-transaction-fields';

// What a per-kind form hands the shell: POST sends it as AddAssetRequest, PUT as UpdateAssetRequest
// (which has no InitialTransaction, so an edit body never carries one).
export type AssetFormBody = UpdateAssetRequest & Pick<AddAssetRequest, 'initialTransaction'>;

// The contract between the shell (AssetFormDialog) and the per-kind form it renders. Each form
// provides itself under ASSET_FORM, so the shell finds whichever one is active with a single
// viewChild(ASSET_FORM) — and owns submit, POST/PUT and ProblemDetails mapping for all of them.
export interface AssetForm {
  readonly form: FormGroup;
  toBody(): AssetFormBody;
  // A reason to refuse submission and show it on the shell's banner, beyond the form's own validity.
  submitBlockedReason?(): string | null;
}

export const ASSET_FORM = new InjectionToken<AssetForm>('AssetForm');

// The key every form nests the first-transaction group under — also the prefix .NET 10's validation
// puts on the nested InitialTransaction.* errors, which the shell routes into that group.
export const INITIAL_TRANSACTION_KEY = 'initialTransaction';

// Create always sends InitialTransaction (null while the box is unchecked); edit never does.
export function assetFormBody(
  fields: UpdateAssetRequest,
  initialTransaction: FirstTransactionGroup,
  isEdit: boolean,
): AssetFormBody {
  return isEdit
    ? fields
    : { ...fields, initialTransaction: buildInitialTransaction(initialTransaction) };
}
