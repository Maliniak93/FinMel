import { Component, computed, effect, input } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';

import type { RecordTransactionRequest } from '../../../../../api/portfolio';
import { toDateOnly } from '../../../../../shared/date-only';
import { formatAmount, formatRate } from '../../../../../shared/format';
import {
  allowedTransactionTypes,
  isPricedTransactionType,
  quantityFieldLabel,
  TRANSACTION_TYPE_BUY,
  TRANSACTION_TYPE_DEPOSIT,
  TRANSACTION_TYPE_SELL,
} from '../../../../transactions/transaction-type';
import { ASSET_CLASS } from '../../../asset-class';

const PLN = 'PLN';

type PaymentCurrency = 'asset' | typeof PLN;

function isTrade(type: number): boolean {
  return type === TRANSACTION_TYPE_BUY || type === TRANSACTION_TYPE_SELL;
}

export function createFirstTransactionGroup(fb: FormBuilder, currencyValued = false) {
  const type = fb.nonNullable.control(
    currencyValued ? TRANSACTION_TYPE_DEPOSIT : TRANSACTION_TYPE_BUY,
    [Validators.required],
  );
  const paymentCurrency = fb.nonNullable.control<PaymentCurrency>('asset');
  const amountPln = fb.control<number | null>(null, [
    (control) =>
      paymentCurrency.value === PLN && isTrade(type.value) && !(Number(control.value) > 0)
        ? { required: true }
        : null,
  ]);
  // Disabling the group emits per control; revalidating then would re-enable the parent's status.
  const revalidate = () => {
    if (type.enabled && amountPln.enabled) {
      amountPln.updateValueAndValidity();
    }
  };
  type.valueChanges.subscribe(revalidate);
  paymentCurrency.valueChanges.subscribe(revalidate);

  const group = fb.nonNullable.group({
    type,
    quantity: [0, [Validators.min(0)]],
    unitPrice: [currencyValued ? 1 : 0, [Validators.min(0)]],
    feeAmount: [null as number | null, [Validators.min(0)]],
    paymentCurrency,
    amountPln,
    date: [new Date(), [Validators.required]],
  });
  group.disable();
  return group;
}

export type FirstTransactionGroup = ReturnType<typeof createFirstTransactionGroup>;

export function buildInitialTransaction(
  group: FirstTransactionGroup,
): RecordTransactionRequest | null {
  if (group.disabled) {
    return null;
  }

  const values = group.getRawValue();
  const priced = isPricedTransactionType(values.type);
  return {
    type: values.type,
    quantity: values.quantity,
    unitPrice: priced ? values.unitPrice : 1,
    date: toDateOnly(values.date),
    ...(priced && values.feeAmount !== null ? { feeAmount: values.feeAmount } : {}),
    ...(values.paymentCurrency === PLN && isTrade(values.type) && values.amountPln !== null
      ? { amountPln: values.amountPln }
      : {}),
  };
}

@Component({
  selector: 'app-first-transaction-fields',
  imports: [
    ReactiveFormsModule,
    MatCheckboxModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    TranslocoPipe,
  ],
  templateUrl: './first-transaction-fields.html',
  styleUrl: './first-transaction-fields.scss',
})
export class FirstTransactionFields {
  readonly group = input.required<FirstTransactionGroup>();
  readonly openingDeposit = input(false);
  readonly currency = input<string>();

  protected readonly transactionTypes = allowedTransactionTypes(ASSET_CLASS.Stock);
  protected readonly isPriced = isPricedTransactionType;
  protected readonly quantityLabel = quantityFieldLabel;
  protected readonly formatAmount = formatAmount;
  protected readonly formatRate = formatRate;
  protected readonly plnCode = PLN;

  protected readonly offersPlnPayment = computed(() => {
    const currency = this.currency();
    return !this.openingDeposit() && !!currency && currency !== PLN;
  });

  constructor() {
    effect(() => {
      if (!this.offersPlnPayment()) {
        this.group().controls.paymentCurrency.setValue('asset');
      }
    });
  }

  protected isTrade = isTrade;

  // Display only: the server derives the stored rate from the same amount.
  protected plnHint(): { price: number; rate: number } | null {
    const values = this.group().getRawValue();
    const amount = Number(values.amountPln);
    if (!(values.quantity > 0) || !(values.unitPrice > 0) || !(amount > 0)) {
      return null;
    }
    return { price: amount / values.quantity, rate: amount / (values.quantity * values.unitPrice) };
  }

  protected toggle(checked: boolean): void {
    if (checked) {
      this.group().enable();
    } else {
      this.group().disable();
    }
  }
}
