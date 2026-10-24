import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

import { formatPercent } from '../format';

export interface SplitBarSegment {
  readonly key: string;
  readonly label: string;
  readonly value: number;
  readonly color: string;
}

@Component({
  selector: 'app-split-bar',
  templateUrl: './split-bar.html',
  styleUrl: './split-bar.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SplitBar {
  readonly segments = input.required<readonly SplitBarSegment[]>();

  protected readonly description = computed(() => {
    const segments = this.segments();
    const total = segments.reduce((sum, segment) => sum + segment.value, 0);
    return segments
      .map(
        (segment) =>
          `${segment.label} ${formatPercent(total > 0 ? (segment.value / total) * 100 : 0)}`,
      )
      .join(', ');
  });
}
