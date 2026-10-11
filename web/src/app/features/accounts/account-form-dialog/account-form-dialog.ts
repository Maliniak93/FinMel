import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  postApiPortfolioAccounts,
  putApiPortfolioAccountsById,
  type AccountResponse,
  type AccountType,
  type TaxWrapper,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { ACCOUNT_TYPE, ACCOUNT_TYPES, TAX_WRAPPER, TAX_WRAPPERS } from '../account-type';

export interface AccountFormDialogData {
  account?: AccountResponse;
}

@Component({
  selector: 'app-account-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoPipe,
  ],
  templateUrl: './account-form-dialog.html',
  styleUrl: './account-form-dialog.scss',
})
export class AccountFormDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<AccountFormDialog>);
  protected readonly data = inject<AccountFormDialogData>(MAT_DIALOG_DATA);

  protected readonly isEdit = !!this.data.account;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly types = ACCOUNT_TYPES;
  protected readonly wrappers = TAX_WRAPPERS;

  protected readonly form = this.formBuilder.nonNullable.group({
    name: [this.data.account?.name ?? '', [Validators.required, Validators.maxLength(200)]],
    type: [this.data.account?.type ?? (ACCOUNT_TYPE.Brokerage as AccountType)],
    wrapper: [this.data.account?.wrapper ?? (TAX_WRAPPER.Regular as TaxWrapper)],
  });

  constructor() {
    if (this.isEdit) {
      this.form.controls.type.disable();
      this.form.controls.wrapper.disable();
    }
  }

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
    const result = this.data.account
      ? await putApiPortfolioAccountsById({
          path: { id: this.data.account.id },
          body: { name: values.name },
        })
      : await postApiPortfolioAccounts({
          body: { name: values.name, type: values.type, wrapper: values.wrapper },
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

    if (problem.errorCode === 'Conflict.DuplicateName') {
      this.form.controls.name.setErrors({
        server: problem.detail ?? translate('accounts.form.duplicateName'),
      });
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
