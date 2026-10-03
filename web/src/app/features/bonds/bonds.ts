import { Component } from '@angular/core';
import { MatTabsModule } from '@angular/material/tabs';
import { TranslocoPipe } from '@jsverse/transloco';

import { BondOffer } from './bond-offer/bond-offer';

@Component({
  selector: 'app-bonds',
  imports: [BondOffer, MatTabsModule, TranslocoPipe],
  templateUrl: './bonds.html',
  styleUrl: './bonds.scss',
})
export class Bonds {}
