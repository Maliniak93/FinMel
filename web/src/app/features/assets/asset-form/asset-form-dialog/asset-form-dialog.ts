import { Component, computed, inject, resource, signal, viewChild } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import {
  MAT_DIALOG_DATA,
  MatDialog,
  MatDialogModule,
  MatDialogRef,
} from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiPortfolioPortfolios,
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
import { BondPurchaseDialog } from '../../../bonds/bond-purchase-dialog/bond-purchase-dialog';
import { DepositFormDialog } from '../../../deposits/deposit-form-dialog/deposit-form-dialog';
import { SavingsAccountFormDialog } from '../../../deposits/savings-account-form-dialog/savings-account-form-dialog';
import { MetalFormDialog } from '../../../metals/metal-form-dialog/metal-form-dialog';
import { ASSET_CLASS } from '../../asset-class';
import { ASSET_FORM, INITIAL_TRANSACTION_KEY } from '../asset-form';
import { AssetTypePicker } from '../asset-type-picker/asset-type-picker';
import { CashAssetForm } from '../forms/cash-asset-form/cash-asset-form';
import { ManualAssetForm } from '../forms/manual-asset-form/manual-asset-form';
import { SecurityAssetForm } from '../forms/security-asset-form/security-asset-form';

export interface AssetFormDialogData {
  portfolioId?: string;
  asset?: AssetResponse;
  assetClass?: AssetClass;
}

type AssetFormKind = 'cash' | 'security' | 'manual';

function formKindFor(assetClass: AssetClass): AssetFormKind {
  switch (Number(assetClass)) {
    case ASSET_CLASS.Cash:
      return 'cash';
    case ASSET_CLASS.Stock:
    case ASSET_CLASS.Etf:
    case ASSET_CLASS.Crypto:
      return 'security';
    default:
      return 'manual';
  }
}

@Component({
  selector: 'app-asset-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    AssetTypePicker,
    CashAssetForm,
    ManualAssetForm,
    SecurityAssetForm,
    TranslocoPipe,
  ],
  templateUrl: './asset-form-dialog.html',
  styleUrl: './asset-form-dialog.scss',
})
export class AssetFormDialog {
  private readonly dialogRef = inject(MatDialogRef<AssetFormDialog>);
  private readonly dialog = inject(MatDialog);
  protected readonly data = inject<AssetFormDialogData>(MAT_DIALOG_DATA);

  protected readonly isEdit = !!this.data.asset;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly presetClass = this.data.assetClass !== undefined;
  protected readonly choosesPortfolio = !this.data.asset && !this.data.portfolioId;

  protected readonly portfolioForm = new FormGroup({
    portfolioId: new FormControl('', {
      nonNullable: true,
      validators: this.choosesPortfolio ? [Validators.required] : [],
    }),
  });

  protected readonly portfoliosResource = resource({
    params: () => (this.choosesPortfolio ? {} : undefined),
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioPortfolios({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ??
            translate('deposits.form.portfoliosLoadFailed'),
        );
      }
      return (result.data ?? []).filter((portfolio) => !portfolio.isArchived);
    },
  });

  private readonly assetClass = signal<AssetClass | null>(
    this.data.asset?.assetClass ?? this.data.assetClass ?? null,
  );
  protected readonly selection = computed(() => {
    const assetClass = this.assetClass();
    return assetClass === null ? null : { assetClass, kind: formKindFor(assetClass) };
  });

  private readonly activeForm = viewChild(ASSET_FORM);

  protected pick(assetClass: AssetClass): void {
    this.formError.set(null);
    const portfolioId = this.targetPortfolioId();

    if (Number(assetClass) === ASSET_CLASS.Deposit) {
      this.dialog
        .open(DepositFormDialog, { width: '560px', data: { portfolioId } })
        .afterClosed()
        .subscribe((saved: boolean | undefined) => this.dialogRef.close(!!saved));
      return;
    }

    if (Number(assetClass) === ASSET_CLASS.Bond) {
      this.dialog
        .open(BondPurchaseDialog, { width: '560px', data: { portfolioId } })
        .afterClosed()
        .subscribe((saved: boolean | undefined) => this.dialogRef.close(!!saved));
      return;
    }

    if (Number(assetClass) === ASSET_CLASS.Savings) {
      this.dialog
        .open(SavingsAccountFormDialog, { width: '560px', data: { portfolioId } })
        .afterClosed()
        .subscribe((saved: boolean | undefined) => this.dialogRef.close(!!saved));
      return;
    }

    if (Number(assetClass) === ASSET_CLASS.PreciousMetal) {
      this.dialog
        .open(MetalFormDialog, { width: '560px', data: { portfolioId } })
        .afterClosed()
        .subscribe((saved: boolean | undefined) => this.dialogRef.close(!!saved));
      return;
    }

    this.assetClass.set(assetClass);
  }

  protected back(): void {
    this.formError.set(null);
    this.assetClass.set(null);
  }

  // A native submit: the FormGroups live in the child forms, so there is no [formGroup] here to raise (ngSubmit).
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

    const portfolioId = this.targetPortfolioId();
    if (active.form.invalid || !portfolioId) {
      active.form.markAllAsTouched();
      this.portfolioForm.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const body = active.toBody();
    const result = this.data.asset
      ? await putApiPortfolioPortfoliosByPortfolioIdAssetsById({
          path: { portfolioId, id: this.data.asset.id },
          body,
        })
      : await postApiPortfolioPortfoliosByPortfolioIdAssets({
          path: { portfolioId },
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

  private targetPortfolioId(): string | undefined {
    return (
      this.data.portfolioId ??
      this.data.asset?.portfolioId ??
      (this.portfolioForm.controls.portfolioId.value || undefined)
    );
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

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }

  private applyBlockFieldErrors(group: FormGroup, problem: ApiProblemDetails): boolean {
    let matched = applyFieldErrors(group, problem);
    for (const [name, control] of Object.entries(group.controls)) {
      if (control instanceof FormGroup && name !== INITIAL_TRANSACTION_KEY) {
        matched = this.applyBlockFieldErrors(control, problem) || matched;
      }
    }
    return matched;
  }

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
