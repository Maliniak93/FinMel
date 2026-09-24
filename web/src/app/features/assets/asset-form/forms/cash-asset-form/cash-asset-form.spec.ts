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
    it('checked → defaults to Deposit and sends it', async () => {
      await setup(0);

      findControl(component.form, 'name').setValue('Checking account');
      await toggleFirstTransaction(fixture);
      expect(findControl(component.form, 'type').value).toBe(2); // Deposit
      expect(findControl(component.form, 'unitPrice').value).toBe(1);
      findControl(component.form, 'quantity').setValue(1000);

      // transactions-pln-value-and-fee-removal AC13: the first transaction carries no fee.
      expect(component.toBody()).toEqual({
        assetClass: 0,
        name: 'Checking account',
        currency: 'PLN',
        initialTransaction: {
          type: 2,
          quantity: 1000,
          unitPrice: 1,
          date: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/),
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
