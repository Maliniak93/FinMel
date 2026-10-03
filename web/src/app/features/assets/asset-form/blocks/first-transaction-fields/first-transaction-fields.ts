import { Component, input } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { TranslocoPipe } from '@jsverse/transloco';

import type { RecordTransactionRequest } from '../../../../../api/portfolio';
import { toDateOnly } from '../../../../../shared/date-only';
import {
  isPricedTransactionType,
  quantityFieldLabel,
  TRANSACTION_TYPE_BUY,
  TRANSACTION_TYPE_DEPOSIT,
  TRANSACTION_TYPES,
} from '../../../../transactions/transaction-type';

export function createFirstTransactionGroup(fb: FormBuilder, currencyValued = false) {
  const group = fb.nonNullable.group({
    type: [currencyValued ? TRANSACTION_TYPE_DEPOSIT : TRANSACTION_TYPE_BUY, [Validators.required]],
    quantity: [0, [Validators.min(0)]],
    unitPrice: [currencyValued ? 1 : 0, [Validators.min(0)]],
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
  return {
    type: values.type,
    quantity: values.quantity,
    unitPrice: isPricedTransactionType(values.type) ? values.unitPrice : 1,
    date: toDateOnly(values.date),
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

  protected readonly transactionTypes = TRANSACTION_TYPES;
  protected readonly isPriced = isPricedTransactionType;
  protected readonly quantityLabel = quantityFieldLabel;

  protected toggle(checked: boolean): void {
    if (checked) {
      this.group().enable();
    } else {
      this.group().disable();
    }
  }
}
