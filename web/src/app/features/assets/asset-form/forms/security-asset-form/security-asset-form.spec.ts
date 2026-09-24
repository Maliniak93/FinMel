import type { ComponentFixture } from '@angular/core/testing';

import { client as marketDataClient } from '../../../../../api/marketdata/client.gen';
import {
  etfSearchResult,
  findControl,
  instrumentId,
  mountAssetForm,
  pickInstrument,
  renderedText,
  toggleFirstTransaction,
} from '../../testing/asset-form-fixtures';
import { SecurityAssetForm } from './security-asset-form';

// AC-5: the market-instrument (Stock/Etf/Bond/Crypto) form — basics, the instrument picker with the
// ADR-018 custom-ticker panel, and the first transaction. Expected bodies moved unchanged from the
// old dialog's market cases.
describe('SecurityAssetForm', () => {
  let fixture: ComponentFixture<SecurityAssetForm>;
  let component: SecurityAssetForm;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(assetClass: number): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch');
    fixture = await mountAssetForm(SecurityAssetForm, assetClass);
    component = fixture.componentInstance;
  }

  it('refuses to submit without a verified/selected instrument', async () => {
    await setup(3); // Etf

    findControl(component.form, 'name').setValue('World ETF');

    expect(component.form.invalid).toBe(true);
  });

  it('creates a market asset from an autocomplete selection, without copying the quote currency', async () => {
    await setup(3); // Etf

    await pickInstrument(fixture, etfSearchResult);

    // Currency stays the user's own PLN/EUR/USD choice — the instrument's EUR quote currency is never
    // copied in (ValueMarketAsset resolves FX off the instrument, not Asset.Currency).
    expect(findControl(component.form, 'currency').value).toBe('PLN');
    // An empty name is filled from the instrument.
    expect(findControl(component.form, 'name').value).toBe('Vanguard FTSE All-World');
    expect(component.form.valid).toBe(true);
    expect(component.toBody()).toEqual({
      assetClass: 3,
      name: 'Vanguard FTSE All-World',
      currency: 'PLN',
      instrumentId,
      initialTransaction: null,
    });
  });

  it('keeps a name the user already typed when an instrument is picked', async () => {
    await setup(3);

    findControl(component.form, 'name').setValue('My world ETF');
    await pickInstrument(fixture, etfSearchResult);

    expect(component.toBody()).toMatchObject({ name: 'My world ETF', instrumentId });
  });

  it('offers the custom-ticker link', async () => {
    await setup(2); // Stock

    expect(renderedText(fixture)).toContain('Verify a new ticker');
  });

  it('checked first transaction → defaults to Buy with unit price 0', async () => {
    await setup(2); // Stock

    await toggleFirstTransaction(fixture);

    expect(findControl(component.form, 'type').value).toBe(0); // Buy
    expect(findControl(component.form, 'unitPrice').value).toBe(0);
    expect(renderedText(fixture)).toContain('Unit price');
  });
});
