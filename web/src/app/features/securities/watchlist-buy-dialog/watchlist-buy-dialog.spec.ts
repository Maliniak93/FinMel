import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { client as portfolioClient } from '../../../api/portfolio/client.gen';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { formatQuantity } from '../../../shared/format';
import { labelsOf, restoreEnglish, switchLanguage, textOf } from '../../../../testing/i18n';
import { jsonResponse } from '../../assets/asset-form/testing/asset-form-fixtures';
import {
  accountBId,
  holdingInA,
  watchlistAccounts,
  watchlistItem,
} from '../testing/watchlist-fixtures';
import { WatchlistBuyDialog } from './watchlist-buy-dialog';

describe('WatchlistBuyDialog', () => {
  let fixture: ComponentFixture<WatchlistBuyDialog>;
  let dialogRef: { close: ReturnType<typeof vi.fn> };
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeAll(() => {
    portfolioClient.setConfig({ baseUrl: 'https://example.test' });
  });

  afterEach(async () => {
    fetchSpy.mockRestore();
    await restoreEnglish();
  });

  async function setup(item: ReturnType<typeof watchlistItem>): Promise<void> {
    dialogRef = { close: vi.fn() };
    fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockImplementation(async () => jsonResponse(watchlistAccounts));

    await TestBed.configureTestingModule({
      imports: [WatchlistBuyDialog],
      providers: [
        provideI18nTesting(),
        { provide: MAT_DIALOG_DATA, useValue: { item } },
        { provide: MatDialogRef, useValue: dialogRef },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(WatchlistBuyDialog);
    await fixture.whenStable();
    await switchLanguage(fixture, 'pl');
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function optionFor(label: string): HTMLElement {
    const option = Array.from(element().querySelectorAll<HTMLElement>('mat-radio-button')).find(
      (candidate) => textOf(candidate).includes(label),
    );
    if (!option) {
      throw new Error(`No buy target '${label}'.`);
    }
    return option;
  }

  it('lists holdings and unarchived accounts without one', async () => {
    await setup(watchlistItem({ holdings: [holdingInA] }));

    expect(labelsOf(element(), 'mat-radio-button')).toEqual([
      `XTB zwykly · Vanguard FTSE All-World (posiadasz ${formatQuantity(10)})`,
      'XTB IKE · nowa pozycja',
    ]);
  });

  it('closes with the picked target', async () => {
    await setup(watchlistItem({ holdings: [holdingInA] }));

    optionFor('XTB IKE').querySelector<HTMLInputElement>('input')!.click();
    fixture.detectChanges();
    await fixture.whenStable();
    element().querySelector<HTMLButtonElement>('[data-testid="buy-confirm"]')!.click();

    expect(dialogRef.close).toHaveBeenCalledWith(
      expect.objectContaining({ accountId: accountBId, assetId: null }),
    );
  });
});
