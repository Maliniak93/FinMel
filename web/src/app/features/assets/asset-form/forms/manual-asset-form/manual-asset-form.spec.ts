import type { ComponentFixture } from '@angular/core/testing';

import { toDateOnly } from '../../../../../shared/date-only';
import {
  findControl,
  mountAssetForm,
  realEstateAsset,
  renderedText,
} from '../../testing/asset-form-fixtures';
import { ManualAssetForm } from './manual-asset-form';

// AC-4: the manual-valuation (RealEstate/Other) form. Value and date are required here — the old
// dialog only switched those validators on in manual mode; this form has no other mode.
describe('ManualAssetForm', () => {
  let fixture: ComponentFixture<ManualAssetForm>;
  let component: ManualAssetForm;

  async function setup(assetClass: number, asset?: typeof realEstateAsset): Promise<void> {
    fixture = await mountAssetForm(ManualAssetForm, assetClass, asset);
    component = fixture.componentInstance;
  }

  it('requires value + date and submits them', async () => {
    await setup(7); // RealEstate

    findControl(component.form, 'name').setValue('Apartment');
    findControl(component.form, 'manualValue').setValue(650000);

    expect(component.form.valid).toBe(true);
    expect(component.toBody()).toEqual({
      assetClass: 7,
      name: 'Apartment',
      currency: 'PLN',
      manualValue: 650000,
      manualValueDate: toDateOnly(new Date()),
      initialTransaction: null,
    });
  });

  it('sends the picked valuation date as a DateOnly', async () => {
    await setup(8); // Other

    findControl(component.form, 'name').setValue('Painting');
    findControl(component.form, 'manualValue').setValue(12000);
    findControl(component.form, 'manualValueDate').setValue(new Date(2026, 2, 5));

    expect(component.toBody()).toEqual({
      assetClass: 8,
      name: 'Painting',
      currency: 'PLN',
      manualValue: 12000,
      manualValueDate: '2026-03-05',
      initialTransaction: null,
    });
  });

  it('does not submit when the value is missing', async () => {
    await setup(7);

    findControl(component.form, 'name').setValue('Apartment');
    findControl(component.form, 'manualValue').setValue(null);

    expect(component.form.invalid).toBe(true);
  });

  it('does not submit when the valuation date is missing', async () => {
    await setup(7);

    findControl(component.form, 'name').setValue('Apartment');
    findControl(component.form, 'manualValue').setValue(650000);
    findControl(component.form, 'manualValueDate').setValue(null);

    expect(component.form.invalid).toBe(true);
  });

  it('pre-fills value and date from an existing manual asset', async () => {
    await setup(7, realEstateAsset);

    expect(findControl(component.form, 'name').value).toBe('Apartment');
    expect(findControl(component.form, 'manualValue').value).toBe(650000);
    expect(findControl(component.form, 'manualValueDate').value).toEqual(new Date(2020, 5, 15));
    expect((fixture.nativeElement as HTMLElement).querySelector('mat-checkbox')).toBeNull();

    const wire = JSON.parse(JSON.stringify(component.toBody()));
    expect(wire).toEqual({
      assetClass: 7,
      name: 'Apartment',
      currency: 'PLN',
      manualValue: 650000,
      manualValueDate: '2020-06-15',
    });
  });

  it('shows value and date fields, and no instrument picker', async () => {
    await setup(7);

    expect(renderedText(fixture)).toContain('Value');
    expect(renderedText(fixture)).toContain('Valued on');
    expect(renderedText(fixture)).not.toContain('Instrument');
  });
});
