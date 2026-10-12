import { Component, inject, signal } from '@angular/core';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
  type AbstractControl,
  type ValidationErrors,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import { postApiPortfolioAssetsByAssetIdTransactions } from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { toDateOnly } from '../../../shared/date-only';
import {
  TRANSACTION_TYPE_DEPOSIT,
  TRANSACTION_TYPE_WITHDRAW,
} from '../../transactions/transaction-type';

export interface CashOperationDialogData {
  cashAssetId: string;
  kind: 'deposit' | 'withdraw';
}

function today(): Date {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate());
}

@Component({
  selector: 'app-cash-operation-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './cash-operation-dialog.html',
  styleUrl: './cash-operation-dialog.scss',
})
export class CashOperationDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<CashOperationDialog>);
  private readonly data = inject<CashOperationDialogData>(MAT_DIALOG_DATA);

  protected readonly isDeposit = this.data.kind === 'deposit';
  protected readonly maxDate = today();

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    amount: [null as number | null, [Validators.required, Validators.min(0.01)]],
    date: [
      today() as Date | null,
      [
        Validators.required,
        (control: AbstractControl): ValidationErrors | null =>
          control.value && (control.value as Date) > this.maxDate ? { dateInFuture: true } : null,
      ],
    ],
  });

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
    const result = await postApiPortfolioAssetsByAssetIdTransactions({
      path: { assetId: this.data.cashAssetId },
      body: {
        type: this.isDeposit ? TRANSACTION_TYPE_DEPOSIT : TRANSACTION_TYPE_WITHDRAW,
        quantity: Number(values.amount),
        unitPrice: 1,
        date: toDateOnly(values.date!),
      },
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
    if (problem.errorCode === 'Validation.InsufficientFunds') {
      this.form.controls.amount.setErrors({ server: translate('accounts.cash.insufficientFunds') });
      this.form.controls.amount.markAsTouched();
      return;
    }

    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
