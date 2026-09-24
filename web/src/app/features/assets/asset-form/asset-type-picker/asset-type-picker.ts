import { Component, output } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

import type { AssetClass } from '../../../../api/portfolio';
import { ASSET_CLASS, ASSET_CLASSES } from '../../asset-class';

const ASSET_CLASS_ICONS: Record<AssetClass, string> = {
  [ASSET_CLASS.Cash]: 'payments',
  [ASSET_CLASS.Deposit]: 'savings',
  [ASSET_CLASS.Stock]: 'show_chart',
  [ASSET_CLASS.Etf]: 'stacked_line_chart',
  [ASSET_CLASS.Bond]: 'receipt_long',
  [ASSET_CLASS.Crypto]: 'currency_bitcoin',
  [ASSET_CLASS.PreciousMetal]: 'diamond',
  [ASSET_CLASS.RealEstate]: 'home',
  [ASSET_CLASS.Other]: 'category',
};

// "New asset" starts here: one tile per AssetClass. The only place a class is chosen — an existing
// asset's class cannot change in the UI.
@Component({
  selector: 'app-asset-type-picker',
  imports: [MatIconModule],
  templateUrl: './asset-type-picker.html',
  styleUrl: './asset-type-picker.scss',
})
export class AssetTypePicker {
  readonly picked = output<AssetClass>();

  protected readonly tiles = ASSET_CLASSES.map((assetClass) => ({
    ...assetClass,
    icon: ASSET_CLASS_ICONS[assetClass.value] ?? 'category',
  }));
}
