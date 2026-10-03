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
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { map } from 'rxjs';

import {
  getApiPortfolioPortfolios,
  getApiPortfolioTransferCandidates,
  postApiPortfolioPortfoliosByPortfolioIdDeposits,
  putApiPortfolioPortfoliosByPortfolioIdDepositsByAssetId,
  type DepositCapitalization,
  type DepositResponse,
  type DepositTermUnit,
  type TransferCandidateResponse,
  type UpdateDepositRequest,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { DEFAULT_CURRENCY, SUPPORTED_CURRENCIES } from '../../../shared/currencies';
import { fromDateOnly, toDateOnly } from '../../../shared/date-only';
import { formatDate, formatMoney } from '../../../shared/format';
import { ASSET_CLASS } from '../../assets/asset-class';
import {
  DEPOSIT_CAPITALIZATIONS,
  DEPOSIT_TERM_UNIT,
  DEPOSIT_TERM_UNITS,
  depositMaturityDate,
  maxTermLength,
} from '../deposit-terms';

export interface DepositFormDialogData {
  portfolioId?: string;
  deposit?: DepositResponse;
}

function termLengthWithinCap(control: AbstractControl): ValidationErrors | null {
  const termUnit = control.parent?.get('termUnit')?.value as DepositTermUnit | undefined;
  if (termUnit === undefined || control.value === null || control.value === '') {
    return null;
  }
  const max = maxTermLength(termUnit);
  return Number(control.value) > max ? { termMax: { max } } : null;
}

@Component({
  selector: 'app-deposit-form-dialog',
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
  templateUrl: './deposit-form-dialog.html',
  styleUrl: './deposit-form-dialog.scss',
})
export class DepositFormDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<DepositFormDialog>);
  protected readonly data = inject<DepositFormDialogData>(MAT_DIALOG_DATA);

  private readonly deposit = this.data.deposit;
  protected readonly isEdit = !!this.deposit;
  private readonly isRolledOver = Number(this.deposit?.rolloverCount ?? 0) > 0;
  protected readonly choosesPortfolio = !this.deposit && !this.data.portfolioId;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly currencies = SUPPORTED_CURRENCIES;
  protected readonly termUnits = DEPOSIT_TERM_UNITS;
  protected readonly capitalizations = DEPOSIT_CAPITALIZATIONS;
  protected readonly formatMoney = formatMoney;
  protected readonly formatDate = formatDate;

  private readonly principalWithinBalance = (control: AbstractControl): ValidationErrors | null => {
    const fundingAssetId = control.parent?.get('fundingAssetId')?.value as
      string | null | undefined;
    if (!fundingAssetId || control.value === null || control.value === '') {
      return null;
    }
    const source = this.fundingSource(fundingAssetId);
    if (!source) {
      return null;
    }
    return Number(control.value) > Number(source.balance)
      ? { exceedsBalance: { balance: source.balance } }
      : null;
  };

  protected readonly form = this.formBuilder.nonNullable.group({
    name: [this.deposit?.name ?? '', [Validators.required, Validators.maxLength(200)]],
    bankName: [this.deposit?.bankName ?? '', [Validators.maxLength(100)]],
    portfolioId: [
      { value: this.data.portfolioId ?? '', disabled: this.isEdit },
      [Validators.required],
    ],
    currency: [
      { value: this.deposit?.currency ?? DEFAULT_CURRENCY, disabled: this.isEdit },
      [Validators.required],
    ],
    principal: [
      {
        value: this.deposit ? Number(this.deposit.principal) : (null as number | null),
        disabled: this.isRolledOver,
      },
      [Validators.required, Validators.min(0.01), this.principalWithinBalance],
    ],
    startDate: [
      {
        value: this.deposit ? fromDateOnly(this.deposit.startDate) : (new Date() as Date | null),
        disabled: this.isRolledOver,
      },
      [Validators.required],
    ],
    termLength: [
      this.deposit ? Number(this.deposit.termLength) : (null as number | null),
      [Validators.required, Validators.min(1), termLengthWithinCap],
    ],
    termUnit: [
      (this.deposit ? Number(this.deposit.termUnit) : DEPOSIT_TERM_UNIT.Months) as DepositTermUnit,
      [Validators.required],
    ],
    annualInterestRatePercent: [
      this.deposit ? Number(this.deposit.annualInterestRatePercent) : (null as number | null),
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],
    capitalization: [
      (this.deposit ? Number(this.deposit.capitalization) : 0) as DepositCapitalization,
      [Validators.required],
    ],
    taxExempt: [this.deposit?.taxExempt ?? false],
    earlyBreakInterestLossPercent: [
      this.deposit ? Number(this.deposit.earlyBreakInterestLossPercent) : (100 as number | null),
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],
    fundingAssetId: [{ value: null as string | null, disabled: this.isEdit }],
  });

  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );

  protected readonly maturityDate = computed(() => {
    const { startDate, termLength, termUnit } = this.formValue();
    const length = Number(termLength);
    if (!startDate || termLength === null || !Number.isInteger(length) || length < 1) {
      return null;
    }
    return depositMaturityDate(startDate, length, termUnit);
  });

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

  private readonly selectedCurrency = computed(() => this.formValue().currency);

  protected readonly fundingCandidatesResource = resource({
    params: () => (this.isEdit ? undefined : { currency: this.selectedCurrency() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioTransferCandidates({
        query: { currency: params.currency, assetClass: ASSET_CLASS.Cash },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('deposits.form.fundingLoadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  constructor() {
    // The cap validators had no parent to read their siblings from while the group was being built.
    this.form.controls.termLength.updateValueAndValidity({ emitEvent: false });
    this.form.controls.termUnit.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.termLength.updateValueAndValidity());
    this.form.controls.fundingAssetId.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.form.controls.principal.updateValueAndValidity());

    effect(() => {
      if (!this.fundingCandidatesResource.hasValue()) {
        return;
      }
      const candidates = this.fundingCandidatesResource.value();
      untracked(() => {
        const fundingAssetId = this.form.controls.fundingAssetId;
        if (
          fundingAssetId.value !== null &&
          !candidates.some((candidate) => candidate.assetId === fundingAssetId.value)
        ) {
          fundingAssetId.setValue(null);
        }
        this.form.controls.principal.updateValueAndValidity();
      });
    });
  }

  private fundingSource(assetId: string): TransferCandidateResponse | undefined {
    return this.fundingCandidatesResource.hasValue()
      ? this.fundingCandidatesResource.value().find((candidate) => candidate.assetId === assetId)
      : undefined;
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
    const terms: UpdateDepositRequest = {
      name: values.name,
      bankName: values.bankName.trim() || null,
      principal: Number(values.principal),
      startDate: toDateOnly(values.startDate!),
      termLength: Number(values.termLength),
      termUnit: values.termUnit,
      annualInterestRatePercent: Number(values.annualInterestRatePercent),
      capitalization: values.capitalization,
      taxExempt: values.taxExempt,
      earlyBreakInterestLossPercent: Number(values.earlyBreakInterestLossPercent),
    };

    const result = this.deposit
      ? await putApiPortfolioPortfoliosByPortfolioIdDepositsByAssetId({
          path: { portfolioId: this.deposit.portfolioId, assetId: this.deposit.assetId },
          body: terms,
        })
      : await postApiPortfolioPortfoliosByPortfolioIdDeposits({
          path: { portfolioId: values.portfolioId },
          body: {
            ...terms,
            currency: values.currency,
            fundingAssetId: values.fundingAssetId ?? undefined,
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
      this.form.controls.principal.setErrors({
        server: problem.detail ?? translate('deposits.form.insufficientFunds'),
      });
      this.form.controls.principal.markAsTouched();
      return;
    }

    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
