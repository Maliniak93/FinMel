import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  putApiPortfolioAllocationBucketsByBucketIdBalancesByMonth,
  type AllocationBucketResponse,
} from '../../../api/portfolio';
import { readProblemDetails } from '../../../core/auth/problem-details';
import { BUCKET_KIND } from '../allocation';
import { MAX_MONTH, MIN_MONTH, monthInRange } from '../recurrence';

export interface BucketBalanceDialogData {
  month: string;
  bucket: AllocationBucketResponse;
}

const MAX_AMOUNT = 10_000_000;

@Component({
  selector: 'app-bucket-balance-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './bucket-balance-dialog.html',
  styleUrl: './bucket-balance-dialog.scss',
})
export class BucketBalanceDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<BucketBalanceDialog>);

  protected readonly data = inject<BucketBalanceDialogData>(MAT_DIALOG_DATA);
  protected readonly minMonth = MIN_MONTH;
  protected readonly maxMonth = MAX_MONTH;
  protected readonly isCushion = Number(this.data.bucket.kind) === BUCKET_KIND.Cushion;

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.group({
    month: [
      this.data.month,
      [Validators.required, Validators.pattern(/^\d{4}-\d{2}$/), monthInRange],
    ],
    amount: this.formBuilder.control<number | null>(null, [
      Validators.required,
      Validators.min(0),
      Validators.max(MAX_AMOUNT),
    ]),
  });

  protected async onSubmit(): Promise<void> {
    if (this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const values = this.form.getRawValue();
    const result = await putApiPortfolioAllocationBucketsByBucketIdBalancesByMonth({
      path: { bucketId: this.data.bucket.bucketId, month: values.month! },
      body: { amount: Number(values.amount) },
    });

    this.submitting.set(false);

    if (result.error) {
      this.formError.set(readProblemDetails(result.error).detail ?? translate('errors.generic'));
      return;
    }

    this.dialogRef.close(true);
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }
}
