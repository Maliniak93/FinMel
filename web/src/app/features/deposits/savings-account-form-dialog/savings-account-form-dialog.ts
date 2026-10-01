import { Component, inject, resource, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  FormControl,
  FormGroup,
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
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoPipe, translate } from '@jsverse/transloco';

import {
  getApiPortfolioPortfolios,
  postApiPortfolioPortfoliosByPortfolioIdSavingsAccounts,
  putApiPortfolioPortfoliosByPortfolioIdSavingsAccountsByAssetId,
  type SavingsAccountResponse,
  type UpdateSavingsAccountRequest,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { DEFAULT_CURRENCY, SUPPORTED_CURRENCIES } from '../../../shared/currencies';
import { toDateOnly } from '../../../shared/date-only';

// Create: no account — `portfolioId` presets the portfolio (the asset type picker), otherwise the
// user picks one. Edit: the account being edited; its portfolio, currency and balance are fixed.
// Payout target (deposit-payout-to-savings): `currency` set — a new account for a deposit payout, in
// that fixed currency, `portfolioId` only the default portfolio, with no opening deposit; it closes
// with the created SavingsAccountResponse instead of `true`.
export interface SavingsAccountFormDialogData {
  portfolioId?: string;
  account?: SavingsAccountResponse;
  currency?: string;
}

// The portfolio, the currency and the optional opening deposit exist on create only: edit leaves
// them out of the form altogether.
interface SavingsAccountForm {
  name: FormControl<string>;
  bankName: FormControl<string>;
  annualInterestRatePercent: FormControl<number | null>;
  taxExempt: FormControl<boolean>;
  portfolioId?: FormControl<string>;
  currency?: FormControl<string>;
  openingAmount?: FormControl<number | null>;
  openingDate?: FormControl<Date | null>;
}

function isBlank(value: unknown): boolean {
  return value === null || value === undefined || value === '';
}

// Empty means "no opening deposit"; anything entered must be above 0.
function positiveWhenEntered(control: AbstractControl): ValidationErrors | null {
  return !isBlank(control.value) && Number(control.value) <= 0 ? { positive: true } : null;
}

// The opening date is needed only with an amount, and — like the server (Europe/Warsaw) — can't be
// later than today. Reads its sibling `openingAmount`, so it is re-run whenever the amount changes.
function openingDateValid(control: AbstractControl): ValidationErrors | null {
  const date = control.value as Date | null;
  if (!date) {
    return isBlank(control.parent?.get('openingAmount')?.value) ? null : { required: true };
  }
  return toDateOnly(date) > toDateOnly(new Date()) ? { future: true } : null;
}

// Create/edit dialog for a savings account (savings-accounts). Control names follow the
// AddSavingsAccountRequest/UpdateSavingsAccountRequest properties, so a server 400 keyed on a field
// lands on it; the opening deposit is the flat pair `openingAmount` + `openingDate`.
@Component({
  selector: 'app-savings-account-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    MatSlideToggleModule,
    TranslocoPipe,
  ],
  templateUrl: './savings-account-form-dialog.html',
  styleUrl: './savings-account-form-dialog.scss',
})
export class SavingsAccountFormDialog {
  private readonly dialogRef = inject(MatDialogRef<SavingsAccountFormDialog>);
  protected readonly data = inject<SavingsAccountFormDialogData>(MAT_DIALOG_DATA);

  private readonly account = this.data.account;
  protected readonly isEdit = !!this.account;
  protected readonly isPayoutTarget = !this.account && !!this.data.currency;
  // The portfolio is chosen here on a plain create and as a payout target (defaulting to the
  // deposit's); the type picker presets it, edit fixes it.
  protected readonly choosesPortfolio =
    !this.account && (!this.data.portfolioId || this.isPayoutTarget);
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly currencies = SUPPORTED_CURRENCIES;

  protected readonly form = new FormGroup<SavingsAccountForm>({
    name: new FormControl(this.account?.name ?? '', {
      nonNullable: true,
      validators: [Validators.required, Validators.maxLength(200)],
    }),
    bankName: new FormControl(this.account?.bankName ?? '', {
      nonNullable: true,
      validators: [Validators.maxLength(100)],
    }),
    annualInterestRatePercent: new FormControl<number | null>(
      this.account ? Number(this.account.annualInterestRatePercent) : null,
      [Validators.required, Validators.min(0), Validators.max(100)],
    ),
    taxExempt: new FormControl(this.account?.taxExempt ?? false, { nonNullable: true }),
    ...(this.account
      ? {}
      : {
          portfolioId: new FormControl(this.data.portfolioId ?? '', {
            nonNullable: true,
            validators: [Validators.required],
          }),
          currency: new FormControl(
            { value: this.data.currency ?? DEFAULT_CURRENCY, disabled: this.isPayoutTarget },
            { nonNullable: true, validators: [Validators.required] },
          ),
          // A payout target starts empty: the payout is its first inflow.
          ...(this.isPayoutTarget
            ? {}
            : {
                openingAmount: new FormControl<number | null>(null, [positiveWhenEntered]),
                openingDate: new FormControl<Date | null>(new Date(), [openingDateValid]),
              }),
        }),
  });

  // Only the plain create offers a portfolio choice — and never an archived one (it is read-only).
  protected readonly portfoliosResource = resource({
    params: () => (this.choosesPortfolio ? {} : undefined),
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioPortfolios({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ??
            translate('deposits.form.portfoliosLoadFailed'),
        );
      }
      return (result.data ?? []).filter((portfolio) => !portfolio.isArchived);
    },
  });

  constructor() {
    this.form.controls.openingAmount?.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.openingDate?.updateValueAndValidity());
  }

  protected hasError(controlName: keyof SavingsAccountForm, errorCode: string): boolean {
    return this.form.get(controlName)?.hasError(errorCode) ?? false;
  }

  protected serverError(controlName: keyof SavingsAccountForm): string | null {
    return (this.form.get(controlName)?.getError('server') as string | undefined) ?? null;
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
    const terms: UpdateSavingsAccountRequest = {
      name: values.name,
      bankName: values.bankName.trim() || null,
      annualInterestRatePercent: Number(values.annualInterestRatePercent),
      taxExempt: values.taxExempt,
    };

    const result = this.account
      ? await putApiPortfolioPortfoliosByPortfolioIdSavingsAccountsByAssetId({
          path: { portfolioId: this.account.portfolioId, assetId: this.account.assetId },
          body: terms,
        })
      : await postApiPortfolioPortfoliosByPortfolioIdSavingsAccounts({
          path: { portfolioId: values.portfolioId! },
          body: {
            ...terms,
            currency: values.currency!,
            // No amount, no opening deposit: the account starts at 0.
            ...(isBlank(values.openingAmount)
              ? {}
              : {
                  openingDeposit: {
                    amount: Number(values.openingAmount),
                    date: toDateOnly(values.openingDate!),
                  },
                }),
          },
        });

    this.submitting.set(false);

    if (result.error) {
      this.applyServerErrors(readProblemDetails(result.error));
      return;
    }

    // A payout target hands the new account back, so the payout can select it.
    this.dialogRef.close(this.isPayoutTarget ? result.data : true);
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
