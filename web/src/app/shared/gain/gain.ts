import { Component, computed, input } from '@angular/core';

import { formatMoney, formatPercent, formatSignedMoney, roundsToZero } from '../format';

@Component({
  selector: 'app-gain',
  templateUrl: './gain.html',
  styleUrl: './gain.scss',
})
export class Gain {
  readonly amount = input<number | null | undefined>();
  readonly percent = input<number | null | undefined>();
  readonly currency = input('PLN');
  readonly dashWithoutPercent = input(false);

  protected readonly present = computed(() => {
    const amount = this.amount();
    return amount !== null && amount !== undefined;
  });

  protected readonly zero = computed(() => roundsToZero(this.amount() ?? 0));

  protected readonly tone = computed<'positive' | 'negative' | 'zero'>(() => {
    if (this.zero()) {
      return 'zero';
    }
    return (this.amount() ?? 0) > 0 ? 'positive' : 'negative';
  });

  protected readonly text = computed(() =>
    this.zero()
      ? formatMoney(0, this.currency())
      : formatSignedMoney(this.amount() ?? 0, this.currency()),
  );

  protected readonly percentText = computed(() => {
    const percent = this.percent();
    if (percent === null || percent === undefined) {
      return this.dashWithoutPercent() ? '(—)' : null;
    }
    return `(${formatPercent(percent)})`;
  });
}
