import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  deleteApiPortfolioAccountsByIdContributionLimitsByYear,
  putApiPortfolioAccountsByIdContributionLimitsByYear,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';

export interface ContributionLimitDialogData {
  accountId: string;
  year: number;
  limitPln: number | null;
}

@Component({
  selector: 'app-contribution-limit-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './contribution-limit-dialog.html',
  styleUrl: './contribution-limit-dialog.scss',
})
export class ContributionLimitDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<ContributionLimitDialog>);
  private readonly data = inject<ContributionLimitDialogData>(MAT_DIALOG_DATA);

  protected readonly hasLimit = this.data.limitPln !== null;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    year: [this.data.year, [Validators.required, Validators.min(2000), Validators.max(2100)]],
    amount: [this.data.limitPln as number | null, [Validators.required, Validators.min(0.01)]],
  });

  protected async onSubmit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const values = this.form.getRawValue();
    await this.run(
      putApiPortfolioAccountsByIdContributionLimitsByYear({
        path: { id: this.data.accountId, year: Number(values.year) },
        body: { amount: Number(values.amount) },
      }),
    );
  }

  protected async onRemove(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    await this.run(
      deleteApiPortfolioAccountsByIdContributionLimitsByYear({
        path: { id: this.data.accountId, year: Number(this.form.getRawValue().year) },
      }),
    );
  }

  protected cancel(): void {
    this.dialogRef.close(false);
  }

  private async run(request: Promise<{ error?: unknown }>): Promise<void> {
    this.submitting.set(true);
    this.formError.set(null);

    const result = await request;

    this.submitting.set(false);

    if (result.error) {
      this.applyServerErrors(readProblemDetails(result.error));
      return;
    }

    this.dialogRef.close(true);
  }

  private applyServerErrors(problem: ApiProblemDetails): void {
    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
