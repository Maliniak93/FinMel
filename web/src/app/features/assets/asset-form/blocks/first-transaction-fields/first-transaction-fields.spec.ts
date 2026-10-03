import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormBuilder } from '@angular/forms';
import { provideNativeDateAdapter } from '@angular/material/core';

import {
  labelsOf,
  polishProblems,
  restoreEnglish,
  switchLanguage,
  TRANSLATIONS,
} from '../../../../../../testing/i18n';
import {
  findControl,
  hasControl,
  showValidationErrors,
  toggleFirstTransaction,
} from '../../testing/asset-form-fixtures';
import {
  buildInitialTransaction,
  createFirstTransactionGroup,
  FirstTransactionFields,
} from './first-transaction-fields';
import { provideI18nTesting } from '../../../../../core/i18n/testing';

describe('FirstTransactionFields', () => {
  let fixture: ComponentFixture<FirstTransactionFields>;
  let group: ReturnType<typeof createFirstTransactionGroup>;

  afterEach(async () => {
    await restoreEnglish();
  });

  async function setup(options: { openingDeposit?: boolean } = {}): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [FirstTransactionFields],
      providers: [provideNativeDateAdapter(), provideI18nTesting()],
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
    findControl(group, 'type').setValue(2);
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
    findControl(group, 'type').setValue(0);
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
      type: 2,
      quantity: 500,
      unitPrice: 1,
      date: '2026-03-05',
    });
  });

  it('has no fee control', async () => {
    await setup();

    await toggleFirstTransaction(fixture);

    expect(hasControl(group, 'fee')).toBe(false);
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('[formcontrolname="fee"]'),
    ).toBeNull();
  });
  describe('in Polish', () => {
    it('renders in Polish', async () => {
      await setup();
      await toggleFirstTransaction(fixture);
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [...labelsOf(element, 'mat-checkbox'), ...labelsOf(element, 'mat-label')];

      const english = texts();
      expect(english).toEqual(['Add first transaction', 'Type', 'Quantity', 'Unit price', 'Date']);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it('renders the opening-deposit variant in Polish', async () => {
      await setup({ openingDeposit: true });
      await toggleFirstTransaction(fixture);
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [...labelsOf(element, 'mat-checkbox'), ...labelsOf(element, 'mat-label')];

      const english = texts();
      expect(english).toEqual(['Add opening deposit', 'Amount', 'Date']);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it('shows the validation messages in Polish, naming the field', async () => {
      await setup();
      await toggleFirstTransaction(fixture);
      findControl(group, 'quantity').setValue(-1);
      findControl(group, 'unitPrice').setValue(-1);
      findControl(group, 'date').setValue(null);
      await showValidationErrors(fixture, group);
      const errors = () => labelsOf(fixture.nativeElement as HTMLElement, 'mat-error');

      const english = errors();
      expect(english).toEqual([
        'Quantity must not be negative.',
        'Unit price must not be negative.',
        'Date is required.',
      ]);

      await switchLanguage(fixture, 'pl');

      const polish = errors();
      expect(polishProblems(english, polish)).toEqual([]);
      expect(polish[0]).toContain(TRANSLATIONS.pl['enums.quantityField.quantity'] as string);
    });

    it('names the amount in the Polish validation message of a non-priced type', async () => {
      await setup();
      await toggleFirstTransaction(fixture);
      findControl(group, 'type').setValue(2);
      findControl(group, 'quantity').setValue(-1);
      await showValidationErrors(fixture, group);
      const errors = () => labelsOf(fixture.nativeElement as HTMLElement, 'mat-error');

      expect(errors()).toEqual(['Amount must not be negative.']);

      await switchLanguage(fixture, 'pl');

      const [message] = errors();
      expect(polishProblems(['Amount must not be negative.'], [message])).toEqual([]);
      expect(message).toContain(TRANSLATIONS.pl['enums.quantityField.amount'] as string);
    });
  });
});
