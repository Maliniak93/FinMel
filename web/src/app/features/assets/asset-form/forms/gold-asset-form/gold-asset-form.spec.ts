import type { ComponentFixture } from '@angular/core/testing';

import { client as marketDataClient } from '../../../../../api/marketdata/client.gen';
import {
  findControl,
  goldSearchResult,
  mountAssetForm,
  pickInstrument,
  renderedText,
} from '../../testing/asset-form-fixtures';
import { GoldAssetForm } from './gold-asset-form';

// AC-7: the PreciousMetal form — the instrument picker without the custom-ticker panel (NBP has no
// arbitrary-ticker lookup, ADR-018/M1.6) plus the old dialog's XAU hint in its place.
describe('GoldAssetForm', () => {
  let fixture: ComponentFixture<GoldAssetForm>;
  let component: GoldAssetForm;
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(() => {
    fetchSpy.mockRestore();
  });

  async function setup(): Promise<void> {
    fetchSpy = vi.spyOn(globalThis, 'fetch');
    fixture = await mountAssetForm(GoldAssetForm, 6); // PreciousMetal
    component = fixture.componentInstance;
  }

  it('renders the XAU hint and no custom-ticker link', async () => {
    await setup();

    expect(renderedText(fixture)).not.toContain('Verify a new ticker');
    expect(renderedText(fixture)).toContain('Precious metal has no custom-ticker lookup');
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
});
