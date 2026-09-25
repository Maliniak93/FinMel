import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';

import {
  postApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactions,
  putApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactionsById,
  type AssetClass,
  type TransactionResponse,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { fromDateOnly, toDateOnly } from '../../../shared/date-only';
import {
  allowedTransactionTypes,
  isPricedTransactionType,
  quantityFieldLabel,
} from '../transaction-type';

export interface TransactionFormDialogData {
  portfolioId: string;
  assetId: string;
  // The asset's class decides which transaction types are offered (cash-transaction-types).
  assetClass: AssetClass;
  transaction?: TransactionResponse;
}

@Component({
  selector: 'app-transaction-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
  ],
  templateUrl: './transaction-form-dialog.html',
  styleUrl: './transaction-form-dialog.scss',
})
export class TransactionFormDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<TransactionFormDialog>);
  protected readonly data = inject<TransactionFormDialogData>(MAT_DIALOG_DATA);

  protected readonly isEdit = !!this.data.transaction;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly transactionTypes = allowedTransactionTypes(this.data.assetClass);

  // Create starts on the first type the class accepts — Deposit for Cash/Deposit, Buy otherwise;
  // edit keeps the stored type.
  protected readonly form = this.formBuilder.nonNullable.group({
    type: [this.data.transaction?.type ?? this.transactionTypes[0].value, [Validators.required]],
    quantity: [Number(this.data.transaction?.quantity ?? 0), [Validators.min(0)]],
    unitPrice: [Number(this.data.transaction?.unitPrice ?? 0), [Validators.min(0)]],
    date: [
      this.data.transaction ? fromDateOnly(this.data.transaction.date) : new Date(),
      [Validators.required],
    ],
  });

  // Signal mirror of the type control so the template can reactively show/hide the unit-price field
  // (signals-first per angular.md, rather than reading form.controls.type.value directly in the
  // template).
  private readonly selectedType = toSignal(this.form.controls.type.valueChanges, {
    initialValue: this.form.controls.type.value,
  });
  protected readonly isPriced = computed(() => isPricedTransactionType(this.selectedType()));
  protected readonly quantityLabel = computed(() => quantityFieldLabel(this.selectedType()));

  protected async onSubmit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const values = this.form.getRawValue();
    const body = {
      type: values.type,
      quantity: values.quantity,
      // Unit price is hidden (and meaningless) for non-trade types — 1 keeps `quantity × unitPrice`
      // a single value formula end to end (transaction-type.ts).
      unitPrice: isPricedTransactionType(values.type) ? values.unitPrice : 1,
      date: toDateOnly(values.date),
    };

    const result = this.data.transaction
      ? await putApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactionsById({
          path: {
            portfolioId: this.data.portfolioId,
            assetId: this.data.assetId,
            id: this.data.transaction.id,
          },
          body,
        })
      : await postApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactions({
          path: { portfolioId: this.data.portfolioId, assetId: this.data.assetId },
          body,
        });

    this.submitting.set(false);

    if (result.error) {
      this.applyServerErrors(readProblemDetails(result.error));
      return;
    }

    this.dialogRef.close(true);
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }

  private applyServerErrors(problem: ApiProblemDetails): void {
    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? 'Something went wrong. Please try again.');
  }
}
