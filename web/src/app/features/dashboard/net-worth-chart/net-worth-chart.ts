import { DatePipe } from '@angular/common';
import { Component, computed, resource, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

import { getApiReportingNetWorthHistory } from '../../../api/reporting';
import { readProblemDetails } from '../../../core/auth/problem-details';

type Range = '1M' | '1Y' | 'YTD' | 'MAX';

const RANGES: readonly Range[] = ['1M', '1Y', 'YTD', 'MAX'];

const CHART_WIDTH = 300;
const CHART_HEIGHT = 100;

interface ChartPoint {
  readonly x: number;
  readonly y: number;
  readonly date: string;
}

@Component({
  selector: 'app-net-worth-chart',
  imports: [DatePipe, MatButtonModule, MatButtonToggleModule, MatProgressSpinnerModule],
  templateUrl: './net-worth-chart.html',
  styleUrl: './net-worth-chart.scss',
})
export class NetWorthChart {
  protected readonly ranges = RANGES;
  protected readonly range = signal<Range>('1Y');
  protected readonly chartWidth = CHART_WIDTH;
  protected readonly chartHeight = CHART_HEIGHT;

  protected readonly historyResource = resource({
    params: () => ({ range: this.range() }),
    loader: async ({ params, abortSignal }) => {
      const result = await getApiReportingNetWorthHistory({
        query: { range: params.range },
        signal: abortSignal,
      });
      if (result.error) {
        throw new Error(
          readProblemDetails(result.error).detail ?? 'Failed to load net-worth history.',
        );
      }
      return result.data;
    },
  });

  // The backend only returns dates with an actual snapshot (NetWorthHistoryResponse doc comment)
  // — connecting them with straight polyline segments is the "visual interpolation across gaps"
  // E5's AC asks for, no client-side padding of missing calendar days required.
  protected readonly points = computed<ChartPoint[]>(() => {
    const rawPoints = this.historyResource.value()?.points ?? [];
    if (rawPoints.length === 0) {
      return [];
    }

    const dates = rawPoints.map((p) => new Date(p.date).getTime());
    const values = rawPoints.map((p) => Number(p.netWorthPln));
    const minDate = Math.min(...dates);
    const maxDate = Math.max(...dates);
    const minValue = Math.min(...values);
    const maxValue = Math.max(...values);
    const dateSpan = maxDate - minDate || 1;

    // Padding zooms the y-axis into the actual value range instead of anchoring at 0 — a wealth
    // chart is read for its fluctuations, which a zero-anchored axis would flatten into a sliver.
    const valueSpan = maxValue - minValue;
    const padding = valueSpan > 0 ? valueSpan * 0.1 : Math.abs(maxValue || 1) * 0.1;
    const paddedMin = minValue - padding;
    const paddedSpan = maxValue + padding - paddedMin || 1;

    return rawPoints.map((p, i) => ({
      x: ((dates[i] - minDate) / dateSpan) * CHART_WIDTH,
      y: CHART_HEIGHT - ((values[i] - paddedMin) / paddedSpan) * CHART_HEIGHT,
      date: p.date,
    }));
  });

  protected readonly linePoints = computed(() =>
    this.points()
      .map((p) => `${p.x.toFixed(2)},${p.y.toFixed(2)}`)
      .join(' '),
  );

  protected readonly asOf = computed(() => {
    const points = this.points();
    return points.length > 0 ? points[points.length - 1].date : null;
  });

  protected setRange(range: Range): void {
    this.range.set(range);
  }
}
