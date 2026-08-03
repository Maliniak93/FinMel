import { Component, computed, input } from '@angular/core';

export interface PieChartSegment {
  readonly label: string;
  /** 0-100, already computed by the caller — this component never sums/divides money. */
  readonly percentage: number;
  readonly color: string;
}

interface RenderedSegment extends PieChartSegment {
  readonly dashArray: string;
  readonly dashOffset: number;
}

// A circle radius that makes the circumference exactly 100, so a percentage (0-100) doubles as a
// dasharray unit with no further scaling.
const RADIUS = 100 / (2 * Math.PI);
const CIRCUMFERENCE = 100;

@Component({
  selector: 'app-pie-chart',
  imports: [],
  templateUrl: './pie-chart.html',
  styleUrl: './pie-chart.scss',
})
export class PieChart {
  readonly segments = input.required<readonly PieChartSegment[]>();

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
