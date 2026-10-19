import { Component, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  postApiPortfolioCashPlaces,
  postApiPortfolioCashPlacesByIdCurrencies,
  putApiPortfolioCashPlacesById,
  type CashPlaceResponse,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { DEFAULT_CURRENCY, SUPPORTED_CURRENCIES } from '../../../shared/currencies';
import { GroupSelect } from '../../../shared/group-select/group-select';

export type CashPlaceFormDialogData =
  | { mode: 'create' }
  | { mode: 'rename'; place: CashPlaceResponse }
  | { mode: 'addCurrency'; place: CashPlaceResponse };

@Component({
  selector: 'app-cash-place-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatCheckboxModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    GroupSelect,
    TranslocoPipe,
  ],
  templateUrl: './cash-place-form-dialog.html',
  styleUrl: './cash-place-form-dialog.scss',
})
export class CashPlaceFormDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<CashPlaceFormDialog>);
  protected readonly data = inject<CashPlaceFormDialogData>(MAT_DIALOG_DATA);

  protected readonly mode = this.data.mode;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly selectedCurrencies = signal<readonly string[]>([DEFAULT_CURRENCY]);
  protected readonly currenciesTouched = signal(false);

  protected readonly currencies = SUPPORTED_CURRENCIES;

  protected readonly missingCurrencies = computed(() => {
    const held = new Set(
      this.data.mode === 'create' ? [] : this.data.place.balances.map((b) => b.currency),
    );
    return SUPPORTED_CURRENCIES.filter((currency) => !held.has(currency.code));
  });

  protected readonly form = this.formBuilder.nonNullable.group({
    name: [
      this.data.mode === 'rename' ? this.data.place.name : '',
      [Validators.required, Validators.maxLength(200)],
    ],
    currency: [this.missingCurrencies()[0]?.code ?? '', [Validators.required]],
    groupId: [(this.data.mode === 'rename' ? this.data.place.groupId : null) ?? null],
  });

  protected readonly noCurrencySelected = computed(() => this.selectedCurrencies().length === 0);

  protected toggleCurrency(code: string, checked: boolean): void {
    this.currenciesTouched.set(true);
    this.selectedCurrencies.update((current) =>
      checked
        ? SUPPORTED_CURRENCIES.map((currency) => currency.code).filter(
            (candidate) => candidate === code || current.includes(candidate),
          )
        : current.filter((candidate) => candidate !== code),
    );
  }

  protected async onSubmit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    const needsName = this.mode !== 'addCurrency';
    const needsCurrency = this.mode === 'addCurrency';
    if (
      (needsName && this.form.controls.name.invalid) ||
      (needsCurrency && this.form.controls.currency.invalid) ||
      (this.mode === 'create' && this.noCurrencySelected())
    ) {
      this.form.markAllAsTouched();
      this.currenciesTouched.set(true);
      return;
    }

    this.submitting.set(true);
    this.formError.set(null);

    const values = this.form.getRawValue();
    const data = this.data;
    const result =
      data.mode === 'create'
        ? await postApiPortfolioCashPlaces({
            body: {
              name: values.name.trim(),
              currencies: [...this.selectedCurrencies()],
              groupId: values.groupId,
            },
          })
        : data.mode === 'rename'
          ? await putApiPortfolioCashPlacesById({
              path: { id: data.place.id },
              body: { name: values.name.trim(), groupId: values.groupId },
            })
          : await postApiPortfolioCashPlacesByIdCurrencies({
              path: { id: data.place.id },
              body: { currency: values.currency },
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
    if (problem.errorCode === 'Conflict.DuplicateName') {
      this.form.controls.name.setErrors({ server: translate('cash.errors.duplicateName') });
      this.form.controls.name.markAsTouched();
      return;
    }

    if (problem.errorCode === 'Conflict.DuplicateCurrency') {
      this.form.controls.currency.setErrors({ server: translate('cash.errors.duplicateCurrency') });
      this.form.controls.currency.markAsTouched();
      return;
    }

    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
