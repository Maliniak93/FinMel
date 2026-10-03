import { Component, output } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';

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
  [ASSET_CLASS.Savings]: 'account_balance',
};

@Component({
  selector: 'app-asset-type-picker',
  imports: [MatIconModule, TranslocoPipe],
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
