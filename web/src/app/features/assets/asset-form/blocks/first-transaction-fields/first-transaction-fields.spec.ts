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

  // `openingDeposit` mirrors how cash-asset-form renders the block (cash-transaction-types): its
  // group is the currency-valued one and the block is told to show an opening deposit.
  async function setup(options: { openingDeposit?: boolean } = {}): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [FirstTransactionFields],
      providers: [provideNativeDateAdapter()],
    }).compileComponents();

    group = createFirstTransactionGroup(
      TestBed.inject(FormBuilder),
      options.openingDeposit ?? false,
    );
    fixture = TestBed.createComponent(FirstTransactionFields);
    fixture.componentRef.setInput('group', group);
    if (options.openingDeposit !== undefined) {
      fixture.componentRef.setInput('openingDeposit', options.openingDeposit);
    }
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

  // cash-transaction-types AC-7: for a cash-like asset the first transaction is always an opening
  // Deposit — only Amount and Date are asked for, and the built transaction is a Deposit at unit
  // price 1.
  it('openingDeposit hides type and price and builds a Deposit', async () => {
    await setup({ openingDeposit: true });
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('mat-checkbox')?.textContent?.trim()).toBe('Add opening deposit');

    await toggleFirstTransaction(fixture);

    expect(element.querySelector('mat-select')).toBeNull();
    expect(element.querySelector('[formcontrolname="type"]')).toBeNull();
    expect(element.querySelector('[formcontrolname="unitPrice"]')).toBeNull();
    expect(element.querySelector('[formcontrolname="quantity"]')).not.toBeNull();
    expect(element.querySelector('[formcontrolname="date"]')).not.toBeNull();
    const fieldLabels = Array.from(element.querySelectorAll('mat-label'), (label) =>
      (label.textContent ?? '').trim(),
    );
    expect(fieldLabels).toEqual(['Amount', 'Date']);

    findControl(group, 'quantity').setValue(500);
    findControl(group, 'date').setValue(new Date(2026, 2, 5));

    expect(buildInitialTransaction(group)).toEqual({
      type: 2, // Deposit
      quantity: 500,
      unitPrice: 1,
      date: '2026-03-05',
    });
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
