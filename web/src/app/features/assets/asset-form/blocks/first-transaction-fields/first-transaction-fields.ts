import { Component, input } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';

import type { RecordTransactionRequest } from '../../../../../api/portfolio';
import { toDateOnly } from '../../../../../shared/date-only';
import {
  isPricedTransactionType,
  quantityFieldLabel,
  TRANSACTION_TYPE_BUY,
  TRANSACTION_TYPE_DEPOSIT,
  TRANSACTION_TYPES,
} from '../../../../transactions/transaction-type';

// "Add first transaction" (M1.5): AddAssetRequest.InitialTransaction. Create only — UpdateAssetRequest
// has no counterpart, so quantity on an existing asset only moves through the transactions view.
//
// The group starts disabled and the checkbox enables it: a disabled group adds nothing to the form's
// validity or value, so an unchecked box never blocks submission and buildInitialTransaction() sends
// null. The pre-fill (Deposit / unit price 1 for a currency-valued asset, Buy / 0 otherwise) is the
// group's initial value — the sub-form is hidden until the box is first checked, and what the user
// typed survives unchecking and checking again.
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
    // Unit price is hidden (and meaningless) for non-trade types — same rule as
    // TransactionFormDialog: 1 keeps `quantity × unitPrice` a single value formula end to end.
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
  ],
  templateUrl: './first-transaction-fields.html',
  styleUrl: './first-transaction-fields.scss',
})
export class FirstTransactionFields {
  readonly group = input.required<FirstTransactionGroup>();

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
