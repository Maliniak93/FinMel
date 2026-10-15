import { Component, computed, effect, inject, resource, signal } from '@angular/core';
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
  getApiPortfolioAccountsById,
  getApiPortfolioTransferCandidates,
  postApiPortfolioAssetsByAssetIdTransactions,
  putApiPortfolioAssetsByAssetIdTransactionsById,
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
import { formatAmount, formatMoney, formatRate } from '../../../shared/format';
import { ASSET_CLASS } from '../../assets/asset-class';
import {
  allowedTransactionTypes,
  isPricedTransactionType,
  isSecurityAssetClass,
  quantityFieldLabel,
  TRANSACTION_TYPE_BUY,
  TRANSACTION_TYPE_DIVIDEND,
  TRANSACTION_TYPE_INTEREST,
  TRANSACTION_TYPE_SELL,
  unitPriceFieldLabel,
} from '../transaction-type';

const CASH_CURRENCY = 'PLN';

const CASH_ERROR_KEYS: Record<string, string> = {
  'Validation.InsufficientFunds': 'transactions.form.insufficientFunds',
  'Validation.FxRateMissing': 'transactions.form.fxRateMissing',
  'Validation.InvalidTransferCounterpart': 'transactions.form.invalidCashAccount',
  'Conflict.TransferLegManaged': 'transactions.form.legManaged',
};

type PaymentCurrency = 'asset' | 'PLN';

interface CashOption {
  assetId: string;
  name: string;
  balance: number | string;
}

export interface TransactionFormDialogData {
  assetId: string;
  assetClass: AssetClass;
  currency?: string;
  accountId?: string | null;
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
    feeAmount: [
      this.data.transaction?.feeAmount != null
        ? Number(this.data.transaction.feeAmount)
        : (null as number | null),
      [Validators.min(0)],
    ],
    cashAssetId: [null as string | null],
    paymentCurrency: [
      (this.data.transaction?.amountPln != null ? 'PLN' : 'asset') as PaymentCurrency,
    ],
    amountPln: [
      this.data.transaction?.amountPln != null
        ? Number(this.data.transaction.amountPln)
        : (null as number | null),
    ],
  });

  private readonly selectedType = toSignal(this.form.controls.type.valueChanges, {
    initialValue: this.form.controls.type.value,
  });
  private readonly selectedPaymentCurrency = toSignal(
    this.form.controls.paymentCurrency.valueChanges,
    { initialValue: this.form.controls.paymentCurrency.value },
  );
  protected readonly isPriced = computed(() => isPricedTransactionType(this.selectedType()));
  protected readonly quantityLabel = computed(() =>
    quantityFieldLabel(this.selectedType(), this.data.assetClass),
  );
  protected readonly unitPriceLabel = unitPriceFieldLabel(this.data.assetClass);

  private readonly isSecurity = isSecurityAssetClass(this.data.assetClass);

  // A new precious-metal trade can move money through a free Cash account.
  private readonly offersCash = !this.isEdit && this.data.assetClass === ASSET_CLASS.PreciousMetal;
  protected readonly showsCash = computed(() => {
    const type = this.selectedType();
    return this.offersCash && (type === TRANSACTION_TYPE_BUY || type === TRANSACTION_TYPE_SELL);
  });
  protected readonly showsFee = computed(() => this.isSecurity && this.isPriced());
  protected readonly cashCurrency = CASH_CURRENCY;
  protected readonly formatMoney = formatMoney;
  protected readonly formatAmount = formatAmount;
  protected readonly formatRate = formatRate;

  private readonly offersPlnPayment =
    this.isSecurity && !!this.data.currency && this.data.currency !== CASH_CURRENCY;
  protected readonly showsPaymentCurrency = computed(() => {
    const type = this.selectedType();
    return (
      this.offersPlnPayment && (type === TRANSACTION_TYPE_BUY || type === TRANSACTION_TYPE_SELL)
    );
  });
  protected readonly paysInPln = computed(
    () => this.showsPaymentCurrency() && this.selectedPaymentCurrency() === CASH_CURRENCY,
  );

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
    params: () => (this.offersCash ? { currency: this.cashCurrency } : undefined),
    loader: async ({ params, abortSignal }): Promise<CashOption[]> => {
      const result = await getApiPortfolioTransferCandidates({
        query: { currency: params.currency, cashPlaces: true },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ??
            translate('transactions.form.cashAccountsLoadFailed'),
        );
      }
      return (result.data ?? []).map((candidate) => ({
        ...candidate,
        name: `${candidate.name} · ${params.currency}`,
      }));
    },
  });

  protected readonly accountResource = resource({
    params: () =>
      this.isSecurity && !this.isEdit && this.data.accountId
        ? { accountId: this.data.accountId }
        : undefined,
    loader: async ({ params, abortSignal }) => {
      const result = await getApiPortfolioAccountsById({
        path: { id: params.accountId },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? translate('transactions.accountLoadFailed'),
        );
      }
      return result.data;
    },
  });

  // Display only, in whole grosze; the amount is known client-side only for a PLN security.
  protected readonly accountCash = computed(() => {
    if (!this.accountResource.hasValue()) {
      return null;
    }
    const account = this.accountResource.value();
    const values = this.formValue();
    const type = Number(values.type);
    const quantity = Number(values.quantity);
    const unitPrice = Number(values.unitPrice);
    const fee = Number(values.feeAmount ?? 0);
    let amount: number | null = null;
    if (type === TRANSACTION_TYPE_DIVIDEND || type === TRANSACTION_TYPE_INTEREST) {
      amount = Math.round(quantity * 100) / 100;
    } else if (
      this.paysInPln() ? Number(values.amountPln) > 0 : this.data.currency === CASH_CURRENCY
    ) {
      const gross = this.paysInPln()
        ? Math.round(Number(values.amountPln) * 100)
        : Math.round(quantity * Math.round(unitPrice * 100));
      const feeGrosze = Math.round(fee * 100);
      amount = (type === TRANSACTION_TYPE_BUY ? gross + feeGrosze : gross - feeGrosze) / 100;
    }
    if (amount !== null && !Number.isFinite(amount)) {
      amount = null;
    }
    const key =
      amount === null
        ? 'transactions.form.accountCashBalance'
        : type === TRANSACTION_TYPE_BUY
          ? 'transactions.form.accountCashTaken'
          : 'transactions.form.accountCashReceived';
    return { key, name: account.name, balance: account.cashBalance, amount };
  });

  // Display only: the server derives the stored rate from the same amount.
  protected readonly plnHint = computed(() => {
    const values = this.formValue();
    const quantity = Number(values.quantity);
    const unitPrice = Number(values.unitPrice);
    const amount = Number(values.amountPln);
    if (!this.paysInPln() || !(quantity > 0) || !(unitPrice > 0) || !(amount > 0)) {
      return null;
    }
    return { price: amount / quantity, rate: amount / (quantity * unitPrice) };
  });

  constructor() {
    if (this.isEdit && this.isSecurity) {
      this.form.controls.type.disable();
    }

    effect(() => {
      const amountPln = this.form.controls.amountPln;
      if (this.paysInPln()) {
        amountPln.setValidators([Validators.required, Validators.min(0.01)]);
      } else {
        amountPln.clearValidators();
      }
      amountPln.updateValueAndValidity({ emitEvent: false });
    });
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
    const feeAmount = this.showsFee() ? values.feeAmount : null;
    const amountPln = this.paysInPln() ? values.amountPln : null;
    const body = {
      type: values.type,
      quantity: values.quantity,
      unitPrice: isPricedTransactionType(values.type) ? values.unitPrice : 1,
      date: toDateOnly(values.date),
      ...(feeAmount !== null ? { feeAmount } : {}),
      ...(amountPln !== null ? { amountPln } : {}),
    };
    const cashAssetId = this.showsCash() ? values.cashAssetId : null;

    const result = this.data.transaction
      ? await putApiPortfolioAssetsByAssetIdTransactionsById({
          path: {
            assetId: this.data.assetId,
            id: this.data.transaction.id,
          },
          body,
        })
      : await postApiPortfolioAssetsByAssetIdTransactions({
          path: { assetId: this.data.assetId },
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
    const cashErrorKey = CASH_ERROR_KEYS[problem.errorCode ?? ''];
    if (cashErrorKey) {
      this.formError.set(translate(cashErrorKey));
      return;
    }

    if (applyFieldErrors(this.form, problem)) {
      return;
    }

    this.formError.set(problem.detail ?? translate('errors.generic'));
  }
}
