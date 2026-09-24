import { TestBed } from '@angular/core/testing';

import type { AssetClass } from '../../../../api/portfolio';
import { ASSET_CLASS, ASSET_CLASSES } from '../../asset-class';
import { AssetTypePicker } from './asset-type-picker';

describe('AssetTypePicker', () => {
  it('renders one tile per asset class and emits the chosen class', async () => {
    await TestBed.configureTestingModule({ imports: [AssetTypePicker] }).compileComponents();
    const fixture = TestBed.createComponent(AssetTypePicker);
    const emitted: AssetClass[] = [];
    fixture.componentInstance.picked.subscribe((assetClass) => emitted.push(assetClass));
    await fixture.whenStable();

    const tiles = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    );

    expect(tiles).toHaveLength(9);
    tiles.forEach((tile, index) => {
      expect(tile.textContent).toContain(ASSET_CLASSES[index].label);
      expect(tile.querySelector('mat-icon')).not.toBeNull();
    });

    tiles[ASSET_CLASSES.findIndex((c) => c.value === ASSET_CLASS.Etf)].click();
    tiles[ASSET_CLASSES.findIndex((c) => c.value === ASSET_CLASS.PreciousMetal)].click();

    expect(emitted).toEqual([ASSET_CLASS.Etf, ASSET_CLASS.PreciousMetal]);
  });
});
