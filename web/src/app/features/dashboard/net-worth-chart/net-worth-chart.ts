import { Component, computed, input, model, output, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoPipe } from '@jsverse/transloco';

import type { NetWorthHistoryPoint } from '../../../api/reporting';
import { fromDateOnly } from '../../../shared/date-only';
import {
  formatAxisDate,
  formatDate,
  formatMoney,
  formatMoneyCompact,
} from '../../../shared/format';
import { niceTicks, xTickUnit, xTicks, type ChartRange } from './chart-scale';

const RANGES: readonly ChartRange[] = ['1M', '1Y', 'YTD', 'MAX'];

const WIDTH = 640;
const HEIGHT = 280;
const MARGIN = { top: 12, right: 16, bottom: 28, left: 64 };
const PLOT_WIDTH = WIDTH - MARGIN.left - MARGIN.right;
const PLOT_HEIGHT = HEIGHT - MARGIN.top - MARGIN.bottom;
const Y_TICK_COUNT = 5;
const Y_LABEL_FRACTION_DIGITS = 2;

interface ChartPoint {
  readonly x: number;
  readonly y: number;
  readonly date: string;
  readonly value: number;
}

@Component({
  selector: 'app-net-worth-chart',
  imports: [MatButtonModule, MatButtonToggleModule, MatProgressSpinnerModule, TranslocoPipe],
  templateUrl: './net-worth-chart.html',
  styleUrl: './net-worth-chart.scss',
})
export class NetWorthChart {
  readonly points = input<readonly NetWorthHistoryPoint[]>([]);
  readonly range = model<ChartRange>('1Y');
  readonly loading = input(false);
  readonly loadError = input<string | null>(null);
  readonly retry = output<void>();

  protected readonly ranges = RANGES;
  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;
  protected readonly margin = MARGIN;
  protected readonly plotWidth = PLOT_WIDTH;
  protected readonly plotHeight = PLOT_HEIGHT;
  protected readonly formatDate = formatDate;
  protected readonly formatMoney = formatMoney;
  protected readonly formatMoneyCompact = formatMoneyCompact;
  protected readonly formatAxisDate = formatAxisDate;
  protected readonly labelFractionDigits = Y_LABEL_FRACTION_DIGITS;

  private readonly hoveredIndex = signal<number | null>(null);

  protected readonly chart = computed(() => {
    const raw = this.points();
    if (raw.length < 2) {
      return null;
    }

    const times = raw.map((p) => fromDateOnly(p.date).getTime());
    const values = raw.map((p) => Number(p.netWorthPln));
    const yTicks = niceTicks(Math.min(...values), Math.max(...values), Y_TICK_COUNT);
    const yMin = yTicks[0];
    const ySpan = yTicks[yTicks.length - 1] - yMin || 1;
    const xMin = times[0];
    const xSpan = times[times.length - 1] - xMin || 1;

    const toX = (time: number) => MARGIN.left + ((time - xMin) / xSpan) * PLOT_WIDTH;
    const toY = (value: number) =>
      MARGIN.top + PLOT_HEIGHT - ((value - yMin) / ySpan) * PLOT_HEIGHT;

    const coordinates: ChartPoint[] = raw.map((p, i) => ({
      x: toX(times[i]),
      y: toY(values[i]),
      date: p.date,
      value: values[i],
    }));

    const line = coordinates.map((c) => `${c.x.toFixed(2)},${c.y.toFixed(2)}`).join('L');
    const bottom = MARGIN.top + PLOT_HEIGHT;
    const first = coordinates[0];
    const last = coordinates[coordinates.length - 1];

    const firstDate = fromDateOnly(raw[0].date);
    const lastDate = fromDateOnly(raw[raw.length - 1].date);
    const range = this.range();

    return {
      times,
      xMin,
      xSpan,
      coordinates,
      linePath: `M${line}`,
      areaPath: `M${line}L${last.x.toFixed(2)},${bottom}L${first.x.toFixed(2)},${bottom}Z`,
      yTicks: yTicks.map((value) => ({ value, y: toY(value) })),
      xTickUnit: xTickUnit(range, firstDate, lastDate),
      xTicks: xTicks(range, firstDate, lastDate).map((date) => ({ date, x: toX(date.getTime()) })),
      first,
      last,
    };
  });

  protected readonly hovered = computed(() => {
    const chart = this.chart();
    const index = this.hoveredIndex();
    return chart && index !== null ? chart.coordinates[index] : null;
  });

  protected tooltipAlignment(point: ChartPoint): 'start' | 'center' | 'end' {
    const ratio = (point.x - MARGIN.left) / PLOT_WIDTH;
    return ratio < 0.2 ? 'start' : ratio > 0.8 ? 'end' : 'center';
  }

  protected onPointerMove(event: PointerEvent): void {
    const chart = this.chart();
    if (!chart) {
      return;
    }
    const bounds = (event.currentTarget as Element).getBoundingClientRect();
    const ratio =
      bounds.width > 0 ? Math.min(1, Math.max(0, (event.clientX - bounds.left) / bounds.width)) : 0;
    const target = chart.xMin + ratio * chart.xSpan;

    let nearest = 0;
    for (let i = 1; i < chart.times.length; i++) {
      if (Math.abs(chart.times[i] - target) < Math.abs(chart.times[nearest] - target)) {
        nearest = i;
      }
    }
    this.hoveredIndex.set(nearest);
  }

  protected onPointerLeave(): void {
    this.hoveredIndex.set(null);
  }
}
