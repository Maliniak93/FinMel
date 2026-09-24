import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormBuilder } from '@angular/forms';
import { provideNativeDateAdapter } from '@angular/material/core';

import { findControl, hasControl, toggleFirstTransaction } from '../../testing/asset-form-fixtures';
import {
  buildInitialTransaction,
  createFirstTransactionGroup,
  FirstTransactionFields,
} from './first-transaction-fields';

// AC-8: the "Add first transaction" block on its own — a checkbox plus the type/quantity/unit
// price/date sub-form, and buildInitialTransaction() turning it into AddAssetRequest.InitialTransaction.
describe('FirstTransactionFields', () => {
  let fixture: ComponentFixture<FirstTransactionFields>;
  let group: ReturnType<typeof createFirstTransactionGroup>;

  async function setup(): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [FirstTransactionFields],
      providers: [provideNativeDateAdapter()],
    }).compileComponents();

    group = createFirstTransactionGroup(TestBed.inject(FormBuilder));
    fixture = TestBed.createComponent(FirstTransactionFields);
    fixture.componentRef.setInput('group', group);
    await fixture.whenStable();
  }

  it('unchecked → buildInitialTransaction returns null', async () => {
    await setup();

    expect(buildInitialTransaction(group)).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('mat-checkbox')).not.toBeNull();
  });

  it('checked with an invalid sub-form → the group is invalid, so submit is blocked', async () => {
    await setup();

    await toggleFirstTransaction(fixture);
    findControl(group, 'quantity').setValue(-5);

    expect(group.invalid).toBe(true);
  });

  it('checked → builds the transaction, with unit price 1 for a non-priced type', async () => {
    await setup();

    await toggleFirstTransaction(fixture);
    findControl(group, 'type').setValue(2); // Deposit
    findControl(group, 'quantity').setValue(1000);
    findControl(group, 'unitPrice').setValue(7);
    findControl(group, 'date').setValue(new Date(2026, 2, 5));

    expect(buildInitialTransaction(group)).toEqual({
      type: 2,
      quantity: 1000,
      unitPrice: 1,
      date: '2026-03-05',
    });
  });

  it('checked → keeps the typed unit price for a priced type (Buy)', async () => {
    await setup();

    await toggleFirstTransaction(fixture);
    findControl(group, 'type').setValue(0); // Buy
    findControl(group, 'quantity').setValue(3);
    findControl(group, 'unitPrice').setValue(10);
    findControl(group, 'date').setValue(new Date(2026, 2, 5));

    expect(buildInitialTransaction(group)).toEqual({
      type: 0,
      quantity: 3,
      unitPrice: 10,
      date: '2026-03-05',
    });
  });

  it('unchecking again → buildInitialTransaction returns null', async () => {
    await setup();

    await toggleFirstTransaction(fixture);
    findControl(group, 'quantity').setValue(5);
    await toggleFirstTransaction(fixture);

    expect(buildInitialTransaction(group)).toBeNull();
  });

  // transactions-pln-value-and-fee-removal AC13: the first-transaction sub-form has no fee.
  it('has no fee control', async () => {
    await setup();

    await toggleFirstTransaction(fixture);

    expect(hasControl(group, 'fee')).toBe(false);
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('[formcontrolname="fee"]'),
    ).toBeNull();
  });
});
