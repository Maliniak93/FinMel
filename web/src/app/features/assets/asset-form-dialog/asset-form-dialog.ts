import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';

import {
  postApiPortfolioPortfoliosByPortfolioIdAssets,
  putApiPortfolioPortfoliosByPortfolioIdAssetsById,
  type AssetResponse,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { ASSET_CLASSES } from '../asset-class';

export interface AssetFormDialogData {
  portfolioId: string;
  asset?: AssetResponse;
}

function toDateOnly(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

// Constructs local midnight for the given calendar date instead of `new Date(dateOnly)`, which
// parses as UTC midnight and shifts a day back in any negative-UTC-offset timezone — the exact
// inverse of toDateOnly() above, so the round-trip never drifts regardless of the user's offset.
function fromDateOnly(dateOnly: string): Date {
  const [year, month, day] = dateOnly.split('-').map(Number);
  return new Date(year, month - 1, day);
}

@Component({
  selector: 'app-asset-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatTooltipModule,
  ],
  templateUrl: './asset-form-dialog.html',
  styleUrl: './asset-form-dialog.scss',
})
export class AssetFormDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<AssetFormDialog>);
  protected readonly data = inject<AssetFormDialogData>(MAT_DIALOG_DATA);

  protected readonly isEdit = !!this.data.asset;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly assetClasses = ASSET_CLASSES;

  protected readonly form = this.formBuilder.nonNullable.group({
    assetClass: [this.data.asset?.assetClass ?? 0, [Validators.required]],
    name: [this.data.asset?.name ?? '', [Validators.required, Validators.maxLength(200)]],
    currency: [
      this.data.asset?.currency ?? 'PLN',
      [Validators.required, Validators.pattern(/^[A-Z]{3}$/)],
    ],
    quantity: [Number(this.data.asset?.quantity ?? 0), [Validators.min(0)]],
    manualValue: [
      Number(this.data.asset?.manualValue ?? 0),
      [Validators.required, Validators.min(0)],
    ],
    manualValueDate: [
      this.data.asset ? fromDateOnly(this.data.asset.manualValueDate) : new Date(),
      [Validators.required],
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
      assetClass: values.assetClass,
      name: values.name,
      currency: values.currency,
      quantity: values.quantity,
      manualValue: values.manualValue,
      manualValueDate: toDateOnly(values.manualValueDate),
    };

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
