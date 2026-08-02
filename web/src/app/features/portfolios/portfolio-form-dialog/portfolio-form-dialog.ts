import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import {
  postApiPortfolioPortfolios,
  putApiPortfolioPortfoliosById,
  type PortfolioResponse,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';

export interface PortfolioFormDialogData {
  portfolio?: PortfolioResponse;
}

@Component({
  selector: 'app-portfolio-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
  ],
  templateUrl: './portfolio-form-dialog.html',
  styleUrl: './portfolio-form-dialog.scss',
})
export class PortfolioFormDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<PortfolioFormDialog>);
  protected readonly data = inject<PortfolioFormDialogData>(MAT_DIALOG_DATA);

  protected readonly isEdit = !!this.data.portfolio;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    name: [this.data.portfolio?.name ?? '', [Validators.required, Validators.maxLength(200)]],
    description: [this.data.portfolio?.description ?? '', [Validators.maxLength(1000)]],
    currency: [
      this.data.portfolio?.currency ?? 'PLN',
      [Validators.required, Validators.pattern(/^[A-Z]{3}$/)],
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
    const body = {
      name: values.name,
      description: values.description || null,
      currency: values.currency,
    };

    const result = this.data.portfolio
      ? await putApiPortfolioPortfoliosById({ path: { id: this.data.portfolio.id }, body })
      : await postApiPortfolioPortfolios({ body });

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

    if (problem.errorCode === 'Conflict.DuplicatePortfolioName') {
      this.form.controls.name.setErrors({
        server: problem.detail ?? 'A portfolio with this name already exists.',
      });
      return;
    }

    this.formError.set(problem.detail ?? 'Something went wrong. Please try again.');
  }
}
