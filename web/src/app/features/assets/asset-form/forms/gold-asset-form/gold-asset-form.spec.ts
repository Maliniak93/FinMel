import type { ComponentFixture } from '@angular/core/testing';

import { client as marketDataClient } from '../../../../../api/marketdata/client.gen';
import {
  attributesOf,
  labelsOf,
  polishProblems,
  restoreEnglish,
  switchLanguage,
} from '../../../../../../testing/i18n';
import {
  findControl,
  goldSearchResult,
  mountAssetForm,
  pickInstrument,
  renderedText,
  showValidationErrors,
  toggleFirstTransaction,
} from '../../testing/asset-form-fixtures';
import { GoldAssetForm } from './gold-asset-form';

describe('GoldAssetForm', () => {
  let fixture: ComponentFixture<GoldAssetForm>;
  let component: GoldAssetForm;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch');
    fixture = await mountAssetForm(GoldAssetForm, 6);
    component = fixture.componentInstance;
  }

  it('renders the XAU hint and no custom-ticker link', async () => {
    await setup();

    expect(renderedText(fixture)).not.toContain('Verify a new ticker');
    expect(renderedText(fixture)).toContain('grams of fine metal');
  });

  it('refuses to submit without a selected instrument', async () => {
    await setup();

    findControl(component.form, 'name').setValue('Gold bars');

    expect(component.form.invalid).toBe(true);
  });

  it('sends the market body for PreciousMetal once an instrument is picked', async () => {
    await setup();

    await pickInstrument(fixture, goldSearchResult);

    expect(component.form.valid).toBe(true);
    expect(component.toBody()).toEqual({
      assetClass: 6,
      name: 'Gold',
      currency: 'PLN',
      instrumentId: goldSearchResult.id,
      initialTransaction: null,
    });
  });
  describe('in Polish', () => {
    it('renders in Polish', async () => {
      await setup();
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...labelsOf(element, 'mat-label'),
        ...attributesOf(element, 'input[placeholder]', 'placeholder'),
        ...labelsOf(element, '.asset-form__hint'),
        ...labelsOf(element, 'mat-checkbox'),
      ];

      const english = texts();
      expect(english).toEqual([
        'Name',
        'Currency',
        'Instrument',
        'Search by ticker or name',
        'Pick an existing instrument above, or verify a new ticker below — creation is blocked until one is selected.',
        'Enter the quantity in grams of fine metal — it is priced at the world spot rate in USD, converted to PLN, not at the NBP gold price.',
        'Add first transaction',
      ]);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });

    it('shows the picked instrument and the first-transaction fields in Polish', async () => {
      await setup();
      await pickInstrument(fixture, goldSearchResult);
      await toggleFirstTransaction(fixture);
      findControl(component.form, 'quantity').setValue(-1);
      findControl(component.form, 'unitPrice').setValue(-1);
      findControl(component.form, 'date').setValue(null);
      await showValidationErrors(fixture, component.form);
      const element = fixture.nativeElement as HTMLElement;
      const texts = () => [
        ...labelsOf(element, '.asset-form__initial-transaction mat-label'),
        ...labelsOf(element, 'mat-error'),
      ];

      const english = texts();
      expect(english).toEqual([
        'Type',
        'Quantity',
        'Unit price',
        'Date',
        'Quantity must not be negative.',
        'Unit price must not be negative.',
        'Date is required.',
      ]);

      await switchLanguage(fixture, 'pl');

      expect(polishProblems(english, texts())).toEqual([]);
    });
  });
});
