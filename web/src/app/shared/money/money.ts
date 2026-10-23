import { Component, computed, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoPipe } from '@jsverse/transloco';

import { formatMoney, formatSignedMoney, roundsToZero } from '../format';

@Component({
  selector: 'app-money',
  imports: [MatIconModule, TranslocoPipe],
  templateUrl: './money.html',
  styleUrl: './money.scss',
})
export class Money {
  readonly amount = input<number | null | undefined>();
  readonly currency = input('PLN');
  readonly kind = input<'value' | 'change'>('value');
  readonly estimatePln = input<number | null | undefined>();
  readonly rateMissing = input(false);
  readonly stale = input(false);

  protected readonly present = computed(() => {
    const amount = this.amount();
    return amount !== null && amount !== undefined;
  });

  protected readonly zero = computed(() => roundsToZero(this.amount() ?? 0));

  protected readonly direction = computed<'up' | 'down' | null>(() => {
    if (this.kind() !== 'change' || this.zero()) {
      return null;
    }
    return (this.amount() ?? 0) > 0 ? 'up' : 'down';
  });

  protected readonly text = computed(() => {
    const amount = this.amount() ?? 0;
    return this.kind() === 'change'
      ? formatSignedMoney(amount, this.currency())
      : formatMoney(amount, this.currency());
  });

  protected readonly approx = ' ≈ ';

  protected readonly foreign = computed(() => this.currency() !== 'PLN');

  protected readonly estimate = computed(() => {
    const estimate = this.estimatePln();
    return this.foreign() && !this.rateMissing() && estimate !== null && estimate !== undefined
      ? formatMoney(estimate)
      : null;
  });
}
