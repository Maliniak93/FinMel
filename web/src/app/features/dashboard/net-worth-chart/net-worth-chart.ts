import {
  Component,
  DestroyRef,
  ElementRef,
  computed,
  effect,
  inject,
  input,
  model,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleGroup, MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoPipe } from '@jsverse/transloco';

import type { AssetClass, NetWorthHistoryPoint } from '../../../api/reporting';
import { SectionError } from '../../../shared/section-error/section-error';
import { fromDateOnly } from '../../../shared/date-only';
import { assetClassColor } from '../../../shared/colors';
import { assetClassLabel } from '../../assets/asset-class';
import {
  formatAxisDate,
  formatDate,
  formatMoney,
  formatMoneyCompact,
} from '../../../shared/format';
import { niceTicks, xTickUnit, xTicks, type ChartRange } from './chart-scale';

const RANGES: readonly ChartRange[] = ['1M', '1Y', 'YTD', 'MAX'];
const CUSTOM_RANGE: ChartRange = 'CUSTOM';

const WIDTH = 640;
const HEIGHT = 280;
const MARGIN = { top: 12, right: 16, bottom: 28, left: 64 };
const Y_TICK_COUNT = 5;
const Y_LABEL_FRACTION_DIGITS = 2;
const BAND_LABEL_MIN_WIDTH = 200;

export interface NetWorthChartGroup {
  readonly key: string;
  readonly label: string;
}

interface ChartPoint {
  readonly x: number;
  readonly y: number;
  readonly date: string;
  readonly value: number;
  readonly isIncomplete: boolean;
}

interface ChartBand {
  readonly x: number;
  readonly width: number;
  readonly lastDate: string;
  readonly showLabel: boolean;
}

@Component({
  selector: 'app-net-worth-chart',
  imports: [
    MatButtonModule,
    MatButtonToggleModule,
    MatIconModule,
    MatMenuModule,
    MatProgressSpinnerModule,
    SectionError,
    TranslocoPipe,
  ],
  templateUrl: './net-worth-chart.html',
  styleUrl: './net-worth-chart.scss',
})
export class NetWorthChart {
  readonly points = input<readonly NetWorthHistoryPoint[]>([]);
  readonly range = model<ChartRange>('1Y');
  readonly rangeLabel = input<string | null>(null);
  readonly firstDate = input<string | null>(null);
  readonly customRangeRequested = output<void>();
  readonly classes = input<readonly AssetClass[]>([]);
  readonly assetClass = model<AssetClass | null>(null);
  readonly groups = input<readonly NetWorthChartGroup[]>([]);
  readonly group = model<string | null>(null);
  readonly loading = input(false);
  readonly loadError = input<string | null>(null);
  readonly retry = output<void>();

  private readonly rangeGroup = viewChild.required(MatButtonToggleGroup);

  protected readonly ranges = RANGES;
  protected readonly customRange = CUSTOM_RANGE;
  protected readonly assetClassLabel = assetClassLabel;
  protected readonly assetClassColor = assetClassColor;
  private readonly canvas = viewChild<ElementRef<HTMLElement>>('canvas');
  private readonly measured = signal<{ width: number; height: number } | null>(null);
  private observer: ResizeObserver | null = null;
  private observed: HTMLElement | null = null;

  private readonly size = computed(() => this.measured() ?? { width: WIDTH, height: HEIGHT });

  protected readonly width = computed(() => this.size().width);
  protected readonly height = computed(() => this.size().height);
  protected readonly margin = MARGIN;
  protected readonly plotWidth = computed(() => this.width() - MARGIN.left - MARGIN.right);
  protected readonly plotHeight = computed(() => this.height() - MARGIN.top - MARGIN.bottom);
  protected readonly formatDate = formatDate;
  protected readonly formatMoney = formatMoney;
  protected readonly formatMoneyCompact = formatMoneyCompact;
  protected readonly formatAxisDate = formatAxisDate;
  protected readonly labelFractionDigits = Y_LABEL_FRACTION_DIGITS;

  protected readonly seriesColor = computed(() => {
    const selected = this.assetClass();
    return selected === null ? null : assetClassColor(selected);
  });

  protected readonly chartLabelKey = computed(() =>
    this.assetClass() === null ? 'netWorthChart.chartLabel' : 'netWorthChart.chartLabelForClass',
  );

  protected readonly classLabelKey = computed(() => {
    const selected = this.assetClass();
    return selected === null ? '' : assetClassLabel(selected);
  });

  protected readonly groupLabel = computed(
    () => this.groups().find((option) => option.key === this.group())?.label ?? null,
  );

  private readonly hoveredIndex = signal<number | null>(null);
  private readonly announcedIndex = signal<number | null>(null);

  constructor() {
    const destroyRef = inject(DestroyRef);
    effect(() => this.observe(this.canvas()?.nativeElement ?? null));
    destroyRef.onDestroy(() => this.observer?.disconnect());
  }

  private observe(element: HTMLElement | null): void {
    if (element === this.observed) {
      return;
    }
    if (this.observed) {
      this.observer?.unobserve(this.observed);
    }
    this.observed = element;
    if (!element || typeof ResizeObserver === 'undefined') {
      return;
    }
    this.observer ??= new ResizeObserver((entries) => {
      const entry = entries.filter((candidate) => candidate.target === this.observed).at(-1);
      if (!entry) {
        return;
      }
      const { width, height } = entry.contentRect;
      this.measured.set(width > 0 && height > 0 ? { width, height } : null);
    });
    this.observer.observe(element);
  }

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

    const plotWidth = this.plotWidth();
    const plotHeight = this.plotHeight();
    const toX = (time: number) => MARGIN.left + ((time - xMin) / xSpan) * plotWidth;
    const toY = (value: number) => MARGIN.top + plotHeight - ((value - yMin) / ySpan) * plotHeight;

    const coordinates: ChartPoint[] = raw.map((p, i) => ({
      x: toX(times[i]),
      y: toY(values[i]),
      date: p.date,
      value: values[i],
      isIncomplete: p.isIncomplete,
    }));

    const bands: ChartBand[] = [];
    let runStart = -1;
    for (let i = 0; i <= coordinates.length; i++) {
      const flagged = i < coordinates.length && coordinates[i].isIncomplete;
      if (flagged && runStart < 0) {
        runStart = i;
      } else if (!flagged && runStart >= 0) {
        const startX = coordinates[runStart].x;
        const width = coordinates[i - 1].x - startX;
        bands.push({
          x: startX,
          width,
          lastDate: coordinates[i - 1].date,
          showLabel: width >= BAND_LABEL_MIN_WIDTH,
        });
        runStart = -1;
      }
    }

    const line = coordinates.map((c) => `${c.x.toFixed(2)},${c.y.toFixed(2)}`).join('L');
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
      bands,
      linePath: `M${line}`,
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

  protected readonly announced = computed(() => {
    const chart = this.chart();
    const index = this.announcedIndex();
    return chart && index !== null ? (chart.coordinates[index] ?? null) : null;
  });

  protected requestCustomRange(): void {
    this.rangeGroup().value = this.range();
    this.customRangeRequested.emit();
  }

  protected selectAll(): void {
    this.assetClass.set(null);
    this.group.set(null);
  }

  protected selectClass(assetClass: AssetClass): void {
    this.assetClass.set(assetClass);
    this.group.set(null);
  }

  protected selectGroup(key: string): void {
    this.group.set(key);
    this.assetClass.set(null);
  }

  protected tooltipAlignment(point: ChartPoint): 'start' | 'center' | 'end' {
    const ratio = (point.x - MARGIN.left) / this.plotWidth();
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

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.clearSelection();
      return;
    }
    const chart = this.chart();
    if (!chart) {
      return;
    }
    const last = chart.coordinates.length - 1;
    const current = this.hoveredIndex();
    let next: number;
    switch (event.key) {
      case 'ArrowLeft':
        next = current === null ? last : Math.max(0, current - 1);
        break;
      case 'ArrowRight':
        next = current === null ? last : Math.min(last, current + 1);
        break;
      case 'Home':
        next = 0;
        break;
      case 'End':
        next = last;
        break;
      default:
        return;
    }
    event.preventDefault();
    this.hoveredIndex.set(next);
    this.announcedIndex.set(next);
  }

  protected clearSelection(): void {
    this.hoveredIndex.set(null);
    this.announcedIndex.set(null);
  }
}
