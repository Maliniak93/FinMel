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
  type TransactionResponse,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import {
  isPricedTransactionType,
  quantityFieldLabel,
  showsFeeField,
  TRANSACTION_TYPES,
} from '../transaction-type';

export interface TransactionFormDialogData {
  portfolioId: string;
  assetId: string;
  transaction?: TransactionResponse;
}

function toDateOnly(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

// Constructs local midnight for the given calendar date instead of `new Date(dateOnly)`, which
// parses as UTC midnight and shifts a day back in any negative-UTC-offset timezone (same helper as
// asset-form-dialog.ts, T1.11).
function fromDateOnly(dateOnly: string): Date {
  const [year, month, day] = dateOnly.split('-').map(Number);
  return new Date(year, month - 1, day);
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
  protected readonly transactionTypes = TRANSACTION_TYPES;

  protected readonly form = this.formBuilder.nonNullable.group({
    type: [this.data.transaction?.type ?? 0, [Validators.required]],
    quantity: [Number(this.data.transaction?.quantity ?? 0), [Validators.min(0)]],
    unitPrice: [Number(this.data.transaction?.unitPrice ?? 0), [Validators.min(0)]],
    fee: [Number(this.data.transaction?.fee ?? 0), [Validators.min(0)]],
    date: [
      this.data.transaction ? fromDateOnly(this.data.transaction.date) : new Date(),
      [Validators.required],
    ],
  });

  // Signal mirror of the type control so the template can reactively show/hide the unit-price and
  // fee fields (signals-first per angular.md, rather than reading form.controls.type.value directly
  // in the template).
  private readonly selectedType = toSignal(this.form.controls.type.valueChanges, {
    initialValue: this.form.controls.type.value,
  });
  protected readonly isPriced = computed(() => isPricedTransactionType(this.selectedType()));
  protected readonly showsFee = computed(() => showsFeeField(this.selectedType()));
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
      // Unit price/fee are hidden (and meaningless) for non-trade types — 1/0 keeps
      // `quantity * unitPrice` a single Value formula end to end (transaction-type.ts).
      unitPrice: isPricedTransactionType(values.type) ? values.unitPrice : 1,
      fee: showsFeeField(values.type) ? values.fee : 0,
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
