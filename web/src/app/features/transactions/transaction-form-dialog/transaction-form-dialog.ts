import { Component, computed, inject, resource, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
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

import {
  getApiPortfolioTransferCandidates,
  postApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactions,
  putApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactionsById,
  type AssetClass,
  type TransactionResponse,
  type TransactionType,
} from '../../../api/portfolio';
import {
  applyFieldErrors,
  readProblemDetails,
  type ApiProblemDetails,
} from '../../../core/auth/problem-details';
import { fromDateOnly, toDateOnly } from '../../../shared/date-only';
import { formatMoney } from '../../../shared/format';
import { ASSET_CLASS } from '../../assets/asset-class';
import {
  allowedTransactionTypes,
  isPricedTransactionType,
  quantityFieldLabel,
  TRANSACTION_TYPE_BUY,
  TRANSACTION_TYPE_SELL,
  unitPriceFieldLabel,
} from '../transaction-type';

const CASH_CURRENCY = 'PLN';

export interface TransactionFormDialogData {
  portfolioId: string;
  assetId: string;
  assetClass: AssetClass;
  transaction?: TransactionResponse;
  type?: TransactionType;
}

@Component({
  selector: 'app-transaction-form-dialog',
  imports: [
    ReactiveFormsModule,
    MatButtonModule,
    MatDatepickerModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatProgressSpinnerModule,
    MatSelectModule,
    TranslocoPipe,
  ],
  templateUrl: './transaction-form-dialog.html',
  styleUrl: './transaction-form-dialog.scss',
})
export class TransactionFormDialog {
  private readonly formBuilder = inject(FormBuilder);
  private readonly dialogRef = inject(MatDialogRef<TransactionFormDialog>);
  protected readonly data = inject<TransactionFormDialogData>(MAT_DIALOG_DATA);

  protected readonly isEdit = !!this.data.transaction;
  protected readonly submitting = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly transactionTypes = allowedTransactionTypes(this.data.assetClass);
  private readonly presetType = this.transactionTypes.find(
    (option) => option.value === Number(this.data.type),
  )?.value;

  protected readonly form = this.formBuilder.nonNullable.group({
    type: [
      this.data.transaction?.type ?? this.presetType ?? this.transactionTypes[0].value,
      [Validators.required],
    ],
    quantity: [Number(this.data.transaction?.quantity ?? 0), [Validators.min(0)]],
    unitPrice: [Number(this.data.transaction?.unitPrice ?? 0), [Validators.min(0)]],
    date: [
      this.data.transaction ? fromDateOnly(this.data.transaction.date) : new Date(),
      [Validators.required],
    ],
    cashAssetId: [null as string | null],
  });

  private readonly selectedType = toSignal(this.form.controls.type.valueChanges, {
    initialValue: this.form.controls.type.value,
  });
  protected readonly isPriced = computed(() => isPricedTransactionType(this.selectedType()));
  protected readonly quantityLabel = computed(() =>
    quantityFieldLabel(this.selectedType(), this.data.assetClass),
  );
  protected readonly unitPriceLabel = unitPriceFieldLabel(this.data.assetClass);

  // Only a new precious-metal Buy or Sell can move money through a PLN Cash account.
  private readonly offersCash = !this.isEdit && this.data.assetClass === ASSET_CLASS.PreciousMetal;
  protected readonly showsCash = computed(
    () =>
      this.offersCash &&
      (this.selectedType() === TRANSACTION_TYPE_BUY ||
        this.selectedType() === TRANSACTION_TYPE_SELL),
  );
  protected readonly cashCurrency = CASH_CURRENCY;
  protected readonly formatMoney = formatMoney;

  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );

  // Display only, in whole grosze; the server computes the stored amount.
  protected readonly cashAmount = computed(() => {
    const values = this.formValue();
    const quantity = Number(values.quantity);
    const unitPrice = Number(values.unitPrice);
    if (!values.cashAssetId || !Number.isFinite(quantity) || !Number.isFinite(unitPrice)) {
      return null;
    }
    return Math.round(quantity * Math.round(unitPrice * 100)) / 100;
  });

  protected readonly cashCandidatesResource = resource({
    params: () => (this.offersCash ? { currency: CASH_CURRENCY } : undefined),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioTransferCandidates({
        query: { currency: params.currency, assetClass: ASSET_CLASS.Cash },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ??
            translate('transactions.form.cashAccountsLoadFailed'),
        );
      }
      return result.data ?? [];
    },
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
      type: values.type,
      quantity: values.quantity,
      unitPrice: isPricedTransactionType(values.type) ? values.unitPrice : 1,
      date: toDateOnly(values.date),
    };
    const cashAssetId = this.showsCash() ? values.cashAssetId : null;

    const result = this.data.transaction
      ? await putApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactionsById({
          path: {
            portfolioId: this.data.portfolioId,
            assetId: this.data.assetId,
            id: this.data.transaction.id,
          },
          body,
        })
      : await postApiPortfolioPortfoliosByPortfolioIdAssetsByAssetIdTransactions({
          path: { portfolioId: this.data.portfolioId, assetId: this.data.assetId },
          body: cashAssetId ? { ...body, cashAssetId } : body,
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
