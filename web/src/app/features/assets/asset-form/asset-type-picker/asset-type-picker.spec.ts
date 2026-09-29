import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';

import {
  labelsOf,
  polishProblems,
  restoreEnglish,
  switchLanguage,
} from '../../../../../testing/i18n';
import type { AssetClass } from '../../../../api/portfolio';
import { ASSET_CLASS, ASSET_CLASSES } from '../../asset-class';
import { AssetTypePicker } from './asset-type-picker';
import { provideI18nTesting } from '../../../../core/i18n/testing';

describe('AssetTypePicker', () => {
  afterEach(async () => {
    // Specs share one worker (isolate: false) — never leave Polish active for the next file.
    await restoreEnglish();
  });

  it('renders one tile per asset class and emits the chosen class', async () => {
    await TestBed.configureTestingModule({
      imports: [AssetTypePicker],
      providers: [provideI18nTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(AssetTypePicker);
    const emitted: AssetClass[] = [];
    fixture.componentInstance.picked.subscribe((assetClass) => emitted.push(assetClass));
    await fixture.whenStable();

    const tiles = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    );

    expect(tiles).toHaveLength(9);
    tiles.forEach((tile, index) => {
      expect(tile.textContent).toContain(
        TestBed.inject(TranslocoService).translate(ASSET_CLASSES[index].label),
      );
      expect(tile.querySelector('mat-icon')).not.toBeNull();
    });

    tiles[ASSET_CLASSES.findIndex((c) => c.value === ASSET_CLASS.Etf)].click();
    tiles[ASSET_CLASSES.findIndex((c) => c.value === ASSET_CLASS.PreciousMetal)].click();

    expect(emitted).toEqual([ASSET_CLASS.Etf, ASSET_CLASS.PreciousMetal]);
  });

  // i18n screens (#132) AC-4: the class tiles follow the language.
  it('renders in Polish', async () => {
    await TestBed.configureTestingModule({
      imports: [AssetTypePicker],
      providers: [provideI18nTesting()],
    }).compileComponents();
    const fixture = TestBed.createComponent(AssetTypePicker);
    await fixture.whenStable();
    const tiles = () => labelsOf(fixture.nativeElement as HTMLElement, 'button');

    const english = tiles();
    expect(english).toHaveLength(9);

    await switchLanguage(fixture, 'pl');

    // "ETF" is spelled the same in Polish.
    expect(polishProblems(english, tiles(), ['ETF'])).toEqual([]);
  });
});
