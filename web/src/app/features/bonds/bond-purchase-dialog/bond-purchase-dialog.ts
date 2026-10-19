import { Component, computed, effect, inject, resource, signal, untracked } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe, translate } from '@jsverse/transloco';
import { map } from 'rxjs';

import { getApiMarketdataBondSeries, type BondSeriesListItem } from '../../../api/marketdata';
import {
  getApiPortfolioAccounts,
  postApiPortfolioBonds,
  putApiPortfolioBondsByAssetId,
  type BondResponse,
  type TreasuryBondType,
  type UpdateBondRequest,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { fromDateOnly, toDateOnly } from '../../../shared/date-only';
import { formatMoney } from '../../../shared/format';
import { ACCOUNT_TYPE } from '../../accounts/account-type';
import { assignGroup } from '../../../shared/group-select/assign-group';
import { GroupSelect } from '../../../shared/group-select/group-select';
import { BOND_TYPES, bondTypeInfo } from '../bond-types';

export interface BondPurchaseDialogData {
  accountId?: string;
  bond?: BondResponse;
  seriesCode?: string;
}

const BOND_CURRENCY = 'PLN';

// The series picker's "enter manually" entry; never a real series code, which is always 3 letters + 4 digits.
const MANUAL_SERIES = 'manual';

function emptyToNull(value: unknown): number | null {
  return value === null || value === undefined || value === '' ? null : Number(value);
}

@Component({
  selector: 'app-bond-purchase-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    GroupSelect,
    TranslocoPipe,
  ],
  templateUrl: './bond-purchase-dialog.html',
  styleUrl: './bond-purchase-dialog.scss',
})
export class BondPurchaseDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<BondPurchaseDialog>);
  protected readonly data = inject<BondPurchaseDialogData>(MAT_DIALOG_DATA);

  private readonly bond = this.data.bond;
  protected readonly isEdit = !!this.bond;
  protected readonly choosesAccount = !this.bond && !this.data.accountId;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);

  protected readonly manualSeries = MANUAL_SERIES;
  protected readonly bondTypes = BOND_TYPES;
  protected readonly currency = BOND_CURRENCY;
  protected readonly formatMoney = formatMoney;
  protected readonly today = new Date();

  private pendingSeriesCode = this.data.seriesCode ?? null;
  private prefilledName: string | null = null;

  protected readonly form = this.formBuilder.group({
    series: [null as string | null],
    accountId: this.formBuilder.nonNullable.control(
      { value: this.data.accountId ?? this.bond?.accountId ?? '', disabled: this.isEdit },
      [Validators.required],
    ),
    groupId: [this.bond?.groupId ?? (null as string | null)],
    name: this.formBuilder.nonNullable.control(this.bond?.name ?? '', [
      Validators.required,
      Validators.maxLength(200),
    ]),
    seriesCode: this.formBuilder.nonNullable.control(this.bond?.seriesCode ?? '', [
      Validators.required,
      Validators.pattern(/^[A-Za-z]{3}\d{4}$/),
    ]),
    type: [
      (this.bond ? Number(this.bond.type) : null) as TreasuryBondType | null,
      [Validators.required],
    ],
    purchaseDate: [
      (this.bond ? fromDateOnly(this.bond.purchaseDate) : new Date()) as Date | null,
      [Validators.required],
    ],
    bondCount: [
      (this.bond ? Number(this.bond.bondCount) : null) as number | null,
      [Validators.required, Validators.min(1)],
    ],
    purchasePricePerBond: [
      (this.bond ? Number(this.bond.purchasePricePerBond) : null) as number | null,
      [Validators.required, Validators.min(0.01), Validators.max(100)],
    ],
    firstPeriodRatePercent: [
      (this.bond ? Number(this.bond.firstPeriodRatePercent) : null) as number | null,
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],
    marginPercent: [
      emptyToNull(this.bond?.marginPercent),
      [Validators.min(0), Validators.max(100)],
    ],
    earlyRedemptionFeePerBond: [
      (this.bond ? Number(this.bond.earlyRedemptionFeePerBond) : null) as number | null,
      [Validators.required, Validators.min(0), Validators.max(100)],
    ],
  });

  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );

  protected readonly cost = computed(() => {
    const grosze = this.costInGrosze(this.formValue());
    return grosze === null ? null : grosze / 100;
  });

  private readonly purchaseDate = computed(() => {
    const date = this.formValue().purchaseDate;
    return date ? toDateOnly(date) : null;
  });

  protected readonly offerResource = resource({
    params: () => {
      const onSaleOn = this.purchaseDate();
      return onSaleOn ? { onSaleOn } : undefined;
    },
    loader: async ({ params, abortSignal }) => {
      const result = await getApiMarketdataBondSeries({
        query: { onSaleOn: params.onSaleOn },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('bonds.purchase.offerLoadFailed'),
        );
      }
      return result.data ?? [];
    },
  });

  protected readonly accountsResource = resource({
    params: () => (this.isEdit ? undefined : {}),
    loader: async ({ abortSignal }) => {
      const result = await getApiPortfolioAccounts({ signal: abortSignal });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('bonds.purchase.accountsLoadFailed'),
        );
      }
      return (result.data ?? []).filter(
        (account) => !account.isArchived && Number(account.type) === ACCOUNT_TYPE.Bonds,
      );
    },
  });

  protected readonly accountCash = computed(() => {
    if (!this.accountsResource.hasValue()) {
      return null;
    }
    const accountId = this.formValue().accountId;
    const account = this.accountsResource.value().find((candidate) => candidate.id === accountId);
    return account
      ? { name: account.name, balance: account.cashBalance, amount: this.cost() }
      : null;
  });

  private readonly accountDefaultGroup = computed(() => {
    if (!this.accountsResource.hasValue()) {
      return null;
    }
    const accountId = this.formValue().accountId;
    return (
      this.accountsResource.value().find((candidate) => candidate.id === accountId)?.groupId ?? null
    );
  });

  constructor() {
    const controls = this.form.controls;

    effect(() => {
      const groupId = this.accountDefaultGroup();
      untracked(() => {
        if (!this.isEdit && !controls.groupId.dirty) {
          controls.groupId.setValue(groupId);
        }
      });
    });

    controls.series.valueChanges
      .pipe(takeUntilDestroyed())
      .subscribe((series) => this.applySeries(series));

    effect(() => {
      if (!this.offerResource.hasValue()) {
        return;
      }
      const offer = this.offerResource.value();
      untracked(() => {
        const code = this.pendingSeriesCode;
        if (code && offer.some((series) => series.code === code)) {
          this.pendingSeriesCode = null;
          controls.series.setValue(code);
        }
      });
    });
  }

  protected typeLabel(series: BondSeriesListItem): string {
    return bondTypeInfo(series.type)?.label ?? '';
  }

  private applySeries(code: string | null): void {
    const controls = this.form.controls;
    if (code === MANUAL_SERIES) {
      controls.seriesCode.setValue('');
      controls.type.setValue(null);
      controls.purchasePricePerBond.setValue(null);
      controls.firstPeriodRatePercent.setValue(null);
      controls.marginPercent.setValue(null);
      controls.earlyRedemptionFeePerBond.setValue(null);
      return;
    }

    const series = this.offerResource.hasValue()
      ? this.offerResource.value().find((candidate) => candidate.code === code)
      : undefined;
    if (!series) {
      return;
    }

    controls.seriesCode.setValue(series.code);
    controls.type.setValue(Number(series.type));
    controls.purchasePricePerBond.setValue(Number(series.issuePrice));
    controls.firstPeriodRatePercent.setValue(emptyToNull(series.firstPeriodRatePercent));
    controls.marginPercent.setValue(emptyToNull(series.marginPercent));
    controls.earlyRedemptionFeePerBond.setValue(
      bondTypeInfo(series.type)?.defaultEarlyRedemptionFee ?? null,
    );

    // The name follows the series until the user types one of their own.
    const name = controls.name.value.trim();
    if (!name || name === this.prefilledName) {
      controls.name.setValue(series.code);
      this.prefilledName = series.code;
    }
  }

  // Display only, in whole grosze; the server computes the stored amount.
  private costInGrosze(
    values: { bondCount?: number | null; purchasePricePerBond?: number | null } | undefined,
  ): number | null {
    const count = emptyToNull(values?.bondCount);
    const price = emptyToNull(values?.purchasePricePerBond);
    return count === null || price === null ? null : count * Math.round(price * 100);
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
    const terms: UpdateBondRequest = {
      name: values.name.trim(),
      seriesCode: values.seriesCode.trim().toUpperCase(),
      type: Number(values.type),
      purchaseDate: toDateOnly(values.purchaseDate!),
      bondCount: Number(values.bondCount),
      purchasePricePerBond: Number(values.purchasePricePerBond),
      firstPeriodRatePercent: Number(values.firstPeriodRatePercent),
      marginPercent: emptyToNull(values.marginPercent),
      earlyRedemptionFeePerBond: Number(values.earlyRedemptionFeePerBond),
    };

    const result = this.bond
      ? await putApiPortfolioBondsByAssetId({
          path: { assetId: this.bond.assetId },
          body: terms,
        })
      : await postApiPortfolioBonds({
          body: { ...terms, accountId: values.accountId, groupId: values.groupId },
        });

    if (!result.error && this.bond && values.groupId !== (this.bond.groupId ?? null)) {
      const assignmentError = await assignGroup(this.bond.assetId, values.groupId);
      if (assignmentError) {
        this.submitting.set(false);
        this.formError.set(assignmentError);
        return;
      }
    }

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
      this.formError.set(translate('bonds.insufficientFunds'));
      return;
    }

    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
