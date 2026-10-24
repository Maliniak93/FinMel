import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

import { Money } from '../money/money';

export interface SummaryItem {
  labelKey: string;
  amount: number | null | undefined;
  currency?: string;
  testId?: string;
}

@Component({
  selector: 'app-summary-strip',
  imports: [Money, TranslocoPipe],
  templateUrl: './summary-strip.html',
  styleUrl: './summary-strip.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'data-testid': 'summary-strip' },
})
export class SummaryStrip {
  readonly items = input.required<SummaryItem[]>();
}
