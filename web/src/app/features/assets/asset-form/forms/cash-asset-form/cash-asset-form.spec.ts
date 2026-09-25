import type { ComponentFixture } from '@angular/core/testing';

import {
  cashAsset,
  findControl,
  hasControl,
  mountAssetForm,
  renderedText,
  toggleFirstTransaction,
} from '../../testing/asset-form-fixtures';
import { CashAssetForm } from './cash-asset-form';

// AC-3 / AC-8: the currency-valued (Cash/Deposit) form. toBody() must equal the body the old
// dialog's onSubmit built for the same input — these expected bodies were moved unchanged.
describe('CashAssetForm', () => {
  let fixture: ComponentFixture<CashAssetForm>;
  let component: CashAssetForm;

  async function setup(assetClass: number, asset?: typeof cashAsset): Promise<void> {
    fixture = await mountAssetForm(CashAssetForm, assetClass, asset);
    component = fixture.componentInstance;
  }

  it('creates Cash with just Name + Currency', async () => {
    await setup(0); // Cash

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
    await setup(1); // Deposit

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
    // cash-transaction-types AC-7: a cash-like asset's first transaction is an opening deposit —
    // no type select and no unit price, only Amount and Date — sent as a Deposit at unit price 1.
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

      // transactions-pln-value-and-fee-removal AC13: the first transaction carries no fee.
      expect(component.form.valid).toBe(true);
      expect(component.toBody()).toEqual({
        assetClass,
        name: 'Checking account',
        currency: 'PLN',
        initialTransaction: {
          type: 2, // Deposit
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
      // Compared as it goes over the wire: UpdateAssetRequest has no InitialTransaction at all.
      const wire = JSON.parse(JSON.stringify(component.toBody()));
      expect(wire).not.toHaveProperty('initialTransaction');
      expect(wire).toEqual({ assetClass: 0, name: 'Checking account', currency: 'PLN' });
    });
  });
});
