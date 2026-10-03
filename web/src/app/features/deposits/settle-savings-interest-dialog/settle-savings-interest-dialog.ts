import { Component, computed, effect, inject, resource, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  FormBuilder,
  ReactiveFormsModule,
  Validators,
  type AbstractControl,
  type ValidationErrors,
} from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { map } from 'rxjs';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiPortfolioPortfoliosByPortfolioIdSavingsAccountsByAssetIdInterestPreview,
  postApiPortfolioPortfoliosByPortfolioIdSavingsAccountsByAssetIdInterestSettlements,
  type SavingsAccountResponse,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { formatDate, formatMoney, formatPercent } from '../../../shared/format';
import { settlementAmounts } from '../deposit-terms';

export interface SettleSavingsInterestDialogData {
  account: SavingsAccountResponse;
}

function taxWithinGross(control: AbstractControl): ValidationErrors | null {
  const gross = control.parent?.get('grossInterest')?.value as number | null | undefined;
  if (gross === null || gross === undefined || control.value === null || control.value === '') {
    return null;
  }
  return Number(control.value) > Number(gross) ? { taxOverGross: true } : null;
}

function isAmount(value: number | string | null): value is number | string {
  return value !== null && value !== '' && Number.isFinite(Number(value));
}

@Component({
  selector: 'app-settle-savings-interest-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    TranslocoPipe,
  ],
  templateUrl: './settle-savings-interest-dialog.html',
  styleUrl: './settle-savings-interest-dialog.scss',
})
export class SettleSavingsInterestDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<SettleSavingsInterestDialog>);
  protected readonly account = inject<SettleSavingsInterestDialogData>(MAT_DIALOG_DATA).account;

  protected readonly formatMoney = formatMoney;
  protected readonly formatDate = formatDate;
  protected readonly formatPercent = formatPercent;

  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly form = this.formBuilder.nonNullable.group({
    grossInterest: [null as number | null, [Validators.required, Validators.min(0)]],
    tax: [null as number | null, [Validators.required, Validators.min(0), taxWithinGross]],
  });

  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );

  protected readonly netInterest = computed(() => {
    const { grossInterest, tax } = this.formValue();
    return isAmount(grossInterest) && isAmount(tax)
      ? settlementAmounts(0, grossInterest, tax).netInterest
      : null;
  });

  protected readonly previewResource = resource({
    loader: async ({ abortSignal }) => {
      const result =
        await getApiPortfolioPortfoliosByPortfolioIdSavingsAccountsByAssetIdInterestPreview({
          path: { portfolioId: this.account.portfolioId, assetId: this.account.assetId },
          signal: abortSignal,
        });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ??
            translate('savings.interest.previewLoadFailed'),
        );
      }
      return result.data ?? null;
    },
  });

  private readonly prefillFromPreviewEffect = effect(() => {
    if (!this.previewResource.hasValue()) {
      return;
    }
    const preview = this.previewResource.value();
    if (preview) {
      // Untracked: whatever the form reads while updating must not re-run this effect and overwrite the user's edits.
      untracked(() =>
        this.form.patchValue({
          grossInterest: Number(preview.grossInterest),
          tax: Number(preview.tax),
        }),
      );
    }
  });

  constructor() {
    this.form.controls.grossInterest.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.tax.updateValueAndValidity());
  }

  protected async onSubmit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    const preview = this.previewResource.hasValue() ? this.previewResource.value() : null;
    if (this.form.invalid || !preview) {
      this.form.markAllAsTouched();
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const values = this.form.getRawValue();
    const result =
      await postApiPortfolioPortfoliosByPortfolioIdSavingsAccountsByAssetIdInterestSettlements({
        path: { portfolioId: this.account.portfolioId, assetId: this.account.assetId },
        body: {
          periodEnd: preview.periodEnd,
          grossInterest: Number(values.grossInterest),
          tax: Number(values.tax),
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
    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
