import type { ComponentFixture } from '@angular/core/testing';

import {
  labelsOf,
  polishProblems,
  matchesTranslation,
  restoreEnglish,
  switchLanguage,
} from '../../../../../../testing/i18n';
import {
  cashAsset,
  findControl,
  hasControl,
  mountAssetForm,
  renderedText,
  showValidationErrors,
  toggleFirstTransaction,
} from '../../testing/asset-form-fixtures';
import { CashAssetForm } from './cash-asset-form';

describe('CashAssetForm', () => {
  let fixture: ComponentFixture<CashAssetForm>;
  let component: CashAssetForm;

  afterEach(async () => {
    await restoreEnglish();
  });

  async function setup(assetClass: number, asset?: typeof cashAsset): Promise<void> {
    fixture = await mountAssetForm(CashAssetForm, assetClass, asset);
    component = fixture.componentInstance;
  }

  it('creates Cash with just Name + Currency', async () => {
    await setup(0);

    findControl(component.form, 'name').setValue('Checking account');

    expect(component.form.valid).toBe(true);
    expect(component.toBody()).toEqual({
      assetClass: 0,
      name: 'Checking account',
      currency: 'PLN',
      initialTransaction: null,
    });
  });

  it('creates Deposit through the same form', async () => {
    await setup(1);

    findControl(component.form, 'name').setValue('Savings deposit');
    findControl(component.form, 'currency').setValue('EUR');

    expect(component.toBody()).toEqual({
      assetClass: 1,
      name: 'Savings deposit',
      currency: 'EUR',
      initialTransaction: null,
    });
  });

  it('shows the currency-valued hint and no manual-value or instrument fields', async () => {
    await setup(0);

    expect(renderedText(fixture)).toContain('Valued automatically from');
    expect(hasControl(component.form, 'manualValue')).toBe(false);
    expect(renderedText(fixture)).not.toContain('Instrument');
  });

  it('blocks submit without a name', async () => {
    await setup(0);

    expect(component.form.invalid).toBe(true);
  });

  describe('add first transaction (create only)', () => {
    it.each([
      { assetClass: 0, className: 'Cash' },
      { assetClass: 1, className: 'Deposit' },
    ])('sends an opening deposit ($className)', async ({ assetClass }) => {
      await setup(assetClass);
      const element = fixture.nativeElement as HTMLElement;

      findControl(component.form, 'name').setValue('Checking account');
      expect(renderedText(fixture)).toContain('Add opening deposit');
      await toggleFirstTransaction(fixture);

      expect(element.querySelector('mat-select[formcontrolname="type"]')).toBeNull();
      expect(element.querySelector('[formcontrolname="unitPrice"]')).toBeNull();

      findControl(component.form, 'quantity').setValue(500);
      findControl(component.form, 'date').setValue(new Date(2026, 2, 5));

      expect(component.form.valid).toBe(true);
      expect(component.toBody()).toEqual({
        assetClass,
        name: 'Checking account',
        currency: 'PLN',
        initialTransaction: {
          type: 2,
          quantity: 500,
          unitPrice: 1,
          date: '2026-03-05',
        },
      });
    });

    it('an invalid initial transaction blocks submission', async () => {
      await setup(0);

      findControl(component.form, 'name').setValue('Checking account');
      await toggleFirstTransaction(fixture);
      findControl(component.form, 'quantity').setValue(-5);

      expect(component.form.invalid).toBe(true);
    });

    it('is not shown / not sent when editing', async () => {
      await setup(0, cashAsset);

      expect((fixture.nativeElement as HTMLElement).querySelector('mat-checkbox')).toBeNull();
      expect(findControl(component.form, 'name').value).toBe('Checking account');
      const wire = JSON.parse(JSON.stringify(component.toBody()));
      expect(wire).not.toHaveProperty('initialTransaction');
      expect(wire).toEqual({ assetClass: 0, name: 'Checking account', currency: 'PLN' });
    });
  });
  describe('in Polish', () => {
    it('renders in Polish', async () => {
      await setup(0);
      await toggleFirstTransaction(fixture);
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [...labelsOf(element, 'mat-label'), ...labelsOf(element, 'mat-checkbox')];
      const hint = () => labelsOf(element, '.asset-form__hint')[0];

      const english = texts();
      expect(english).toEqual(['Name', 'Currency', 'Amount', 'Date', 'Add opening deposit']);
      expect(hint()).toMatch(/^Valued automatically from PLN/);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
      expect(hint()).toContain('PLN');
      expect(hint()).not.toMatch(/^Valued automatically/);
      expect(matchesTranslation('pl', hint()), `"${hint()}" is not a pl.json value`).toBe(true);
    });

    it('shows client-side validation in Polish', async () => {
      await setup(0);
      await toggleFirstTransaction(fixture);
      findControl(component.form, 'quantity').setValue(-5);
      findControl(component.form, 'date').setValue(null);
      await showValidationErrors(fixture, component.form);
      const errors = () => labelsOf(fixture.nativeElement as HTMLElement, 'mat-error');

      const english = errors();
      expect(english).toEqual([
        'Name is required.',
        'Amount must not be negative.',
        'Date is required.',
      ]);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, errors())).toEqual([]);
    });
  });
});
