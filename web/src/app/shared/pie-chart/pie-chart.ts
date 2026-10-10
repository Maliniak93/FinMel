import { Component, computed, input, output } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

export interface PieChartSegment {
  readonly key: string;
  readonly label: string;
  readonly percentage: number;
  readonly color: string;
}

interface RenderedSegment extends PieChartSegment {
  readonly dashArray: string;
  readonly dashOffset: number;
}

// A radius whose circumference is exactly 100, so a percentage is a dasharray length.
const RADIUS = 100 / (2 * Math.PI);
const CIRCUMFERENCE = 100;

@Component({
  selector: 'app-pie-chart',
  imports: [TranslocoPipe],
  templateUrl: './pie-chart.html',
  styleUrl: './pie-chart.scss',
})
export class PieChart {
  readonly segments = input.required<readonly PieChartSegment[]>();
  readonly selected = input<string | null>(null);
  readonly segmentClick = output<string>();

  protected readonly radius = RADIUS;

  protected readonly rendered = computed<RenderedSegment[]>(() => {
    let cumulative = 0;
    return this.segments()
      .filter((segment) => segment.percentage > 0)
      .map((segment) => {
        const dashArray = `${segment.percentage} ${CIRCUMFERENCE - segment.percentage}`;
        const dashOffset = -cumulative;
        cumulative += segment.percentage;
        return { ...segment, dashArray, dashOffset };
      });
  });
}
