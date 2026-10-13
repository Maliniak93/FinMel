import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as marketDataClient } from '../../../api/marketdata/client.gen';
import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { restoreEnglish } from '../../../../testing/i18n';
import { ASSET_CLASS } from '../../assets/asset-class';
import {
  findControl,
  jsonResponse,
  pickInstrument,
  stockInstrumentDetails,
} from '../../assets/asset-form/testing/asset-form-fixtures';
import { methodOf, watchlistItem } from '../testing/watchlist-fixtures';
import { WatchlistItemDialog, type WatchlistItemDialogData } from './watchlist-item-dialog';

describe('WatchlistItemDialog', () => {
  let fixture: ComponentFixture<WatchlistItemDialog>;
  let component: WatchlistItemDialog;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
    marketDataClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(
    data: WatchlistItemDialogData,
    fetchImplementation: (input: unknown) => Promise<Response>,
  ): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(fetchImplementation);

    await TestBed.configureTestingModule({
      imports: [WatchlistItemDialog],
      providers: [
        provideI18nTesting(),
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(WatchlistItemDialog);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  function writeRequests(): Request[] {
    return fetchSpy.mock.calls
      .map((call: unknown[]) => call[0] as Request)
      .filter((request: Request) => typeof request !== 'string' && request.method !== 'GET');
  }

  it('adds an item', async () => {
    await setup({ assetClass: ASSET_CLASS.Stock }, async (input) =>
      methodOf(input) === 'POST' ? jsonResponse(watchlistItem(), 201) : jsonResponse([]),
    );
    await pickInstrument(fixture, stockInstrumentDetails);
    findControl(component['form'], 'note').setValue('Core position');
    findControl(component['form'], 'targetPrice').setValue(190);

    await component['onSubmit']();

    const [post] = writeRequests();
    expect(post.method).toBe('POST');
    expect(new URL(post.url).pathname).toBe('/api/portfolio/watchlist');
    expect(await post.json()).toEqual(
      expect.objectContaining({
        instrumentId: stockInstrumentDetails.id,
        note: 'Core position',
        targetPrice: 190,
      }),
    );
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });

  it('shows the already-watched error', async () => {
    await setup({ assetClass: ASSET_CLASS.Stock }, async (input) =>
      methodOf(input) === 'POST'
        ? jsonResponse({ title: 'Conflict', status: 409, detail: 'Already watched.' }, 409)
        : jsonResponse([]),
    );
    await pickInstrument(fixture, stockInstrumentDetails);

    await component['onSubmit']();
    fixture.detectChanges();
    await fixture.whenStable();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
    expect(alert?.textContent?.trim()).toBeTruthy();
    expect(dialogRef.close).not.toHaveBeenCalled();
  });

  it('edits an item', async () => {
    const item = watchlistItem();
    await setup({ assetClass: ASSET_CLASS.Etf, item }, async (input) =>
      methodOf(input) === 'PUT' ? jsonResponse(item) : jsonResponse([]),
    );
    findControl(component['form'], 'note').setValue('Sell above 130');
    findControl(component['form'], 'targetPrice').setValue(130);

    await component['onSubmit']();

    const [put] = writeRequests();
    expect(put.method).toBe('PUT');
    expect(new URL(put.url).pathname).toBe(`/api/portfolio/watchlist/${item.id}`);
    expect(await put.json()).toEqual({ note: 'Sell above 130', targetPrice: 130 });
    expect(dialogRef.close).toHaveBeenCalledWith(true);
  });
});
