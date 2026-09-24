import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { FormGroup } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import {
  postApiPortfolioPortfoliosByPortfolioIdAssets,
  putApiPortfolioPortfoliosByPortfolioIdAssetsById,
  type AssetClass,
  type AssetResponse,
} from '../../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../../core/auth/problem-details';
import { ASSET_CLASS } from '../../asset-class';
import { ASSET_FORM, INITIAL_TRANSACTION_KEY } from '../asset-form';
import { AssetTypePicker } from '../asset-type-picker/asset-type-picker';
import { CashAssetForm } from '../forms/cash-asset-form/cash-asset-form';
import { GoldAssetForm } from '../forms/gold-asset-form/gold-asset-form';
import { ManualAssetForm } from '../forms/manual-asset-form/manual-asset-form';
import { SecurityAssetForm } from '../forms/security-asset-form/security-asset-form';

export interface AssetFormDialogData {
  portfolioId: string;
  asset?: AssetResponse;
}

type AssetFormKind = 'cash' | 'security' | 'gold' | 'manual';

// Which per-kind form each AssetClass opens.
function formKindFor(assetClass: AssetClass): AssetFormKind {
  switch (Number(assetClass)) {
    case ASSET_CLASS.Cash:
    case ASSET_CLASS.Deposit:
      return 'cash';
    case ASSET_CLASS.Stock:
    case ASSET_CLASS.Etf:
    case ASSET_CLASS.Bond:
    case ASSET_CLASS.Crypto:
      return 'security';
    case ASSET_CLASS.PreciousMetal:
      return 'gold';
    default:
      return 'manual';
  }
}

// The create/edit shell. Create shows the type picker, then the chosen class's form; edit goes
// straight to the stored class's form, and the class cannot change there. The shell owns the title,
// the actions, POST/PUT with the body the active form builds, and mapping ProblemDetails onto it.
@Component({
  selector: 'app-asset-form-dialog',
  imports: [
    MatButtonModule,
    MatDialogModule,
    MatIconModule,
    MatProgressSpinnerModule,
    AssetTypePicker,
    CashAssetForm,
    GoldAssetForm,
    ManualAssetForm,
    SecurityAssetForm,
  ],
  templateUrl: './asset-form-dialog.html',
  styleUrl: './asset-form-dialog.scss',
})
export class AssetFormDialog {
  private readonly dialogRef = inject(MatDialogRef<AssetFormDialog>);
  protected readonly data = inject<AssetFormDialogData>(MAT_DIALOG_DATA);

  protected readonly isEdit = !!this.data.asset;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  // null while the picker is showing.
  private readonly assetClass = signal<AssetClass | null>(this.data.asset?.assetClass ?? null);
  protected readonly selection = computed(() => {
    const assetClass = this.assetClass();
    return assetClass === null ? null : { assetClass, kind: formKindFor(assetClass) };
  });

  private readonly activeForm = viewChild(ASSET_FORM);

  protected pick(assetClass: AssetClass): void {
    this.formError.set(null);
    this.assetClass.set(assetClass);
  }

  // Back to the picker: the form is destroyed with whatever was typed into it.
  protected back(): void {
    this.formError.set(null);
    this.assetClass.set(null);
  }

  // A native submit (the Save button, or Enter in a field): the forms' FormGroups live in the child
  // components, so there is no [formGroup] here to raise (ngSubmit).
  protected submit(event: Event): void {
    event.preventDefault();
    void this.onSubmit();
  }

  protected async onSubmit(): Promise<void> {
    const active = this.activeForm();
    if (!active || this.submitting()) {
      return;
    }

    const blockedReason = active.submitBlockedReason?.() ?? null;
    if (blockedReason) {
      this.formError.set(blockedReason);
      return;
    }

    if (active.form.invalid) {
      active.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const body = active.toBody();
    const result = this.data.asset
      ? await putApiPortfolioPortfoliosByPortfolioIdAssetsById({
          path: { portfolioId: this.data.portfolioId, id: this.data.asset.id },
          body,
        })
      : await postApiPortfolioPortfoliosByPortfolioIdAssets({
          path: { portfolioId: this.data.portfolioId },
          body,
        });

    this.submitting.set(false);

    if (result.error) {
      this.applyServerErrors(active.form, readProblemDetails(result.error));
      return;
    }

    this.dialogRef.close(true);
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }

  private applyServerErrors(form: FormGroup, problem: ApiProblemDetails): void {
    const mainFieldMatched = this.applyBlockFieldErrors(form, problem);
    const transaction = form.get(INITIAL_TRANSACTION_KEY);
    const transactionFieldMatched =
      transaction instanceof FormGroup &&
      this.applyInitialTransactionFieldErrors(transaction, problem);

    if (mainFieldMatched || transactionFieldMatched) {
      return;
    }

    this.formError.set(problem.detail ?? 'Something went wrong. Please try again.');
  }

  // A form nests each block's group (basics, manual value, …), so a top-level key like "Name" is
  // matched against every group in the tree — except the first-transaction sub-form, whose keys
  // arrive prefixed and are routed by applyInitialTransactionFieldErrors.
  private applyBlockFieldErrors(group: FormGroup, problem: ApiProblemDetails): boolean {
    let matched = applyFieldErrors(group, problem);
    for (const [name, control] of Object.entries(group.controls)) {
      if (control instanceof FormGroup && name !== INITIAL_TRANSACTION_KEY) {
        matched = this.applyBlockFieldErrors(control, problem) || matched;
      }
    }
    return matched;
  }

  // AddAssetRequest.InitialTransaction is a nested complex property — .NET 10's validation recurses
  // into it and reports keys like "InitialTransaction.Quantity", which applyFieldErrors (matching
  // plain control names only) can't route on its own; this matches the suffix against the
  // first-transaction group's own controls instead.
  private applyInitialTransactionFieldErrors(
    transaction: FormGroup,
    problem: ApiProblemDetails,
  ): boolean {
    if (!problem.errors) {
      return false;
    }

    let matched = false;
    for (const [field, messages] of Object.entries(problem.errors)) {
      if (!field.toLowerCase().includes('initialtransaction')) {
        continue;
      }
      const suffix = field.split('.').pop() ?? field;
      const controlName = Object.keys(transaction.controls).find(
        (name) => name.toLowerCase() === suffix.toLowerCase(),
      );
      if (controlName) {
        transaction.get(controlName)?.setErrors({ server: messages.join(' ') });
        matched = true;
      }
    }
    return matched;
  }
}
