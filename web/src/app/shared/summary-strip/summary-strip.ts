import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';

import { Gain } from '../gain/gain';
import { Money } from '../money/money';

export interface SummaryGain {
  amount: number | null;
  percent: number | null;
  captionKey: string;
  excluded?: { key: string; count: number };
}

export interface SummaryItem {
  labelKey: string;
  amount: number | null | undefined;
  gain?: SummaryGain;
  currency?: string;
  testId?: string;
  link?: { labelKey: string; route: string };
}

@Component({
  selector: 'app-summary-strip',
  imports: [Gain, Money, RouterLink, TranslocoPipe],
  templateUrl: './summary-strip.html',
  styleUrl: './summary-strip.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'data-testid': 'summary-strip' },
})
export class SummaryStrip {
  readonly items = input.required<SummaryItem[]>();
}
