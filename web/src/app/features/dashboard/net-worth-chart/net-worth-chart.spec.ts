import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { ComponentFixture, TestBed } from '@angular/core/testing';
import { vi } from 'vitest';

import {
  attributesOf,
  polishProblems,
  restoreEnglish,
  switchLanguage,
  textOf,
  textsOf,
} from '../../../../testing/i18n';

import type { NetWorthHistoryResponse } from '../../../api/reporting';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { formatDate, formatMoney } from '../../../shared/format';
import { ASSET_CLASS } from '../../assets/asset-class';
import { menuItem, openFilterMenu } from '../testing/dashboard-fixtures';
import { niceTicks } from './chart-scale';
import { NetWorthChart } from './net-worth-chart';

function normalised(text: string): string {
  return text.replace(/\s+/g, ' ');
}

const ETF_COLOUR = /var\(--sk-c-etf\)/;
const CLASSES = [ASSET_CLASS.Etf, ASSET_CLASS.Stock, ASSET_CLASS.Cash];

function paintOf(element: Element | null): string {
  if (!element) {
    return '';
  }
  const style = getComputedStyle(element);
  return [
    element.getAttribute('stroke'),
    element.getAttribute('fill'),
    element.getAttribute('style'),
    style.stroke,
    style.fill,
  ].join(' ');
}

function textOutsideControls(element: HTMLElement): string {
  const clone = element.cloneNode(true) as HTMLElement;
  clone
    .querySelectorAll('mat-chip-listbox, mat-button-toggle-group, svg, .net-worth-chart__tooltip')
    .forEach((node) => node.remove());
  return normalised(clone.textContent ?? '');
}

function hoverPlot(element: HTMLElement, clientX: number): void {
  const plot = element.querySelector('.net-worth-chart__plot') as SVGElement;
  plot.getBoundingClientRect = () =>
    ({ left: 0, right: 300, top: 0, bottom: 100, width: 300, height: 100, x: 0, y: 0 }) as DOMRect;
  plot.dispatchEvent(new MouseEvent('pointermove', { clientX, clientY: 50, bubbles: true }));
}

type HistoryPoints = NetWorthHistoryResponse['points'];

const yearPoints: HistoryPoints = [
  { date: '2026-06-01', netWorthPln: 10000, isIncomplete: false },
  { date: '2026-07-01', netWorthPln: 11000, isIncomplete: false },
  { date: '2026-08-01', netWorthPln: 10500, isIncomplete: false },
];

const monthPoints: HistoryPoints = Array.from({ length: 20 }, (_, i) => ({
  date: `2026-08-${String(i + 1).padStart(2, '0')}`,
  netWorthPln: 10000 + i * 100,
  isIncomplete: false,
}));

function dailyPoints(
  month: string,
  count: number,
  incomplete: (index: number) => boolean,
): HistoryPoints {
  return Array.from({ length: count }, (_, i) => ({
    date: `${month}-${String(i + 1).padStart(2, '0')}`,
    netWorthPln: 10000 + i * 100,
    isIncomplete: incomplete(i),
  }));
}

const PLOT_LEFT = 64;
const PLOT_WIDTH = 560;

function bandsOf(element: HTMLElement): SVGRectElement[] {
  return [...element.querySelectorAll<SVGRectElement>('.net-worth-chart__band')];
}

describe('NetWorthChart', () => {
  let fixture: ComponentFixture<NetWorthChart>;

  afterEach(async () => {
    vi.unstubAllGlobals();
    await restoreEnglish();
  });

  interface FakeObserver {
    callback: ResizeObserverCallback;
    targets: Element[];
    disconnected: boolean;
  }

  function stubResizeObserver(): FakeObserver[] {
    const observers: FakeObserver[] = [];
    vi.stubGlobal(
      'ResizeObserver',
      class {
        private readonly state: FakeObserver;
        constructor(callback: ResizeObserverCallback) {
          this.state = { callback, targets: [], disconnected: false };
          observers.push(this.state);
        }
        observe(target: Element): void {
          this.state.targets.push(target);
        }
        unobserve(target: Element): void {
          this.state.targets = this.state.targets.filter((candidate) => candidate !== target);
        }
        disconnect(): void {
          this.state.disconnected = true;
        }
      },
    );
    return observers;
  }

  async function resizeTo(observers: FakeObserver[], width: number, height: number): Promise<void> {
    for (const observer of observers) {
      const entries = observer.targets.map(
        (target) =>
          ({
            target,
            contentRect: {
              width,
              height,
              x: 0,
              y: 0,
              top: 0,
              left: 0,
              right: width,
              bottom: height,
            },
            contentBoxSize: [{ inlineSize: width, blockSize: height }],
            borderBoxSize: [{ inlineSize: width, blockSize: height }],
          }) as unknown as ResizeObserverEntry,
      );
      observer.callback(entries, {} as ResizeObserver);
    }
    await fixture.whenStable();
  }

  function stylesheet(): string {
    return readFileSync(
      resolve(process.cwd(), 'src/app/features/dashboard/net-worth-chart/net-worth-chart.scss'),
      'utf8',
    );
  }

  function ruleOf(scss: string, selector: string): string {
    const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return [
      ...scss.matchAll(new RegExp(`(?:^|[\\s,}])${escaped}\\s*(?:,[^{]*)?\\{([^}]*)\\}`, 'g')),
    ]
      .map((match) => match[1])
      .join(' ');
  }

  async function setup(
    points: HistoryPoints,
    range = '1Y',
    classes?: readonly number[],
    assetClass?: number | null,
    firstDate?: string | null,
  ): Promise<HTMLElement> {
    await TestBed.configureTestingModule({
      imports: [NetWorthChart],
      providers: [provideI18nTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(NetWorthChart);
    fixture.componentRef.setInput('points', points);
    fixture.componentRef.setInput('range', range);
    if (classes) {
      fixture.componentRef.setInput('classes', classes);
    }
    if (assetClass !== undefined) {
      fixture.componentRef.setInput('assetClass', assetClass);
    }
    if (firstDate !== undefined) {
      fixture.componentRef.setInput('firstDate', firstDate);
    }
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  function radios(element: HTMLElement): HTMLButtonElement[] {
    return [
      ...element.querySelectorAll<HTMLButtonElement>('mat-button-toggle button[role="radio"]'),
    ];
  }

  it('range control has a Zakres option beside the presets', async () => {
    const element = await setup(yearPoints, '1Y', undefined, undefined, '2025-01-10');

    const options = radios(element);

    expect(options).toHaveLength(5);
    expect(options.slice(0, 4).map((option) => option.textContent?.trim())).toEqual([
      '1M',
      '1Y',
      'YTD',
      'MAX',
    ]);
    expect(options.map((option) => option.getAttribute('aria-checked'))).toEqual([
      'false',
      'true',
      'false',
      'false',
      'false',
    ]);
    expect(options[4].disabled).toBe(false);

    fixture.componentRef.setInput('range', 'CUSTOM');
    await fixture.whenStable();

    expect(radios(element).map((option) => option.getAttribute('aria-checked'))).toEqual([
      'false',
      'false',
      'false',
      'false',
      'true',
    ]);
  });

  it('Zakres is disabled without history', async () => {
    const element = await setup([], '1Y', undefined, undefined, null);

    const options = radios(element);

    expect(options).toHaveLength(5);
    expect(options[4].disabled).toBe(true);
  });

  it('renders axes and gridlines', async () => {
    const element = await setup(yearPoints);

    const gridlines = element.querySelectorAll('.net-worth-chart__gridline');
    const yLabels = textsOf(element, '.net-worth-chart__y-label');
    const xLabels = textsOf(element, '.net-worth-chart__x-label');
    const line = element.querySelector('path.net-worth-chart__line');

    expect(gridlines.length).toBeGreaterThanOrEqual(4);
    expect(gridlines.length).toBeLessThanOrEqual(6);
    expect(yLabels).toHaveLength(gridlines.length);
    for (const label of yLabels) {
      expect(label).toMatch(/K/);
    }
    expect(xLabels.length).toBeGreaterThanOrEqual(1);
    expect(xLabels.length).toBeLessThanOrEqual(3);
    expect(line?.getAttribute('d')).toBeTruthy();
    expect(element.querySelector('svg')?.getAttribute('aria-label')).toContain(formatMoney(10000));
    expect(element.querySelector('svg')?.getAttribute('aria-label')).toContain(formatMoney(10500));
  });

  it('uses day ticks for 1M', async () => {
    const element = await setup(monthPoints, '1M');

    const xLabels = textsOf(element, '.net-worth-chart__x-label');

    expect(xLabels.length).toBeGreaterThanOrEqual(4);
  });

  it('shows a tooltip for the nearest point', async () => {
    const element = await setup(yearPoints);
    const plot = element.querySelector('.net-worth-chart__plot') as SVGElement;
    plot.getBoundingClientRect = () =>
      ({
        left: 0,
        right: 300,
        top: 0,
        bottom: 100,
        width: 300,
        height: 100,
        x: 0,
        y: 0,
      }) as DOMRect;

    expect(element.querySelector('.net-worth-chart__tooltip')).toBeNull();
    expect(element.querySelector('.net-worth-chart__guide')).toBeNull();
    expect(element.querySelector('.net-worth-chart__marker')).toBeNull();

    plot.dispatchEvent(new MouseEvent('pointermove', { clientX: 0, clientY: 50, bubbles: true }));
    await fixture.whenStable();

    const tooltip = textOf(element.querySelector('.net-worth-chart__tooltip'));
    expect(tooltip).toContain(formatDate('2026-06-01'));
    expect(tooltip).toContain(normalised(formatMoney(10000)));
    expect(element.querySelector('.net-worth-chart__guide')).not.toBeNull();
    expect(element.querySelector('.net-worth-chart__marker')).not.toBeNull();

    plot.dispatchEvent(new MouseEvent('pointermove', { clientX: 300, clientY: 50, bubbles: true }));
    await fixture.whenStable();

    const lastTooltip = textOf(element.querySelector('.net-worth-chart__tooltip'));
    expect(lastTooltip).toContain(formatDate('2026-08-01'));
    expect(lastTooltip).toContain(normalised(formatMoney(10500)));

    plot.dispatchEvent(new MouseEvent('pointerleave', { bubbles: true }));
    await fixture.whenStable();

    expect(element.querySelector('.net-worth-chart__tooltip')).toBeNull();
    expect(element.querySelector('.net-worth-chart__guide')).toBeNull();
    expect(element.querySelector('.net-worth-chart__marker')).toBeNull();
  });

  it('labels and colours the series with the selected class', async () => {
    const element = await setup(yearPoints, '1Y', CLASSES, ASSET_CLASS.Etf);

    expect(paintOf(element.querySelector('path.net-worth-chart__line'))).toMatch(ETF_COLOUR);
    expect(textOutsideControls(element)).toContain('ETF');
    expect(element.querySelector('svg')?.getAttribute('aria-label')).toContain('ETF');

    hoverPlot(element, 0);
    await fixture.whenStable();

    expect(textOf(element.querySelector('.net-worth-chart__tooltip'))).toMatch(/^ETF/);
  });

  it('shows no class label or colour without a selected class', async () => {
    const element = await setup(yearPoints, '1Y', CLASSES, null);

    expect(paintOf(element.querySelector('path.net-worth-chart__line'))).not.toMatch(ETF_COLOUR);
    expect(textOutsideControls(element)).not.toContain('ETF');
    expect(element.querySelector('svg')?.getAttribute('aria-label') ?? '').not.toContain('ETF');

    hoverPlot(element, 0);
    await fixture.whenStable();

    expect(textOf(element.querySelector('.net-worth-chart__tooltip'))).not.toContain('ETF');
  });

  it('not enough history', async () => {
    const element = await setup([{ date: '2026-08-01', netWorthPln: 10000, isIncomplete: false }]);

    expect(textOf(element.querySelector('.net-worth-chart__empty'))).toBe(
      'Not enough history to chart yet.',
    );
    expect(element.querySelector('svg')).toBeNull();

    await switchLanguage(fixture, 'pl');

    expect(
      polishProblems(
        ['Not enough history to chart yet.'],
        [textOf(element.querySelector('.net-worth-chart__empty'))],
      ),
    ).toEqual([]);
  });

  it('renders in Polish', async () => {
    const element = await setup(yearPoints);
    const labels = () => [
      ...attributesOf(element, 'mat-button-toggle-group', 'aria-label'),
      ...attributesOf(element, 'svg', 'aria-label'),
    ];

    const english = labels();
    expect(english).toHaveLength(2);

    await switchLanguage(fixture, 'pl');

    expect(polishProblems(english, labels())).toEqual([]);
    expect(textsOf(element, 'mat-button-toggle')).toEqual(['1M', '1Y', 'YTD', 'MAX', 'Zakres']);
  });

  it('filter menu picks one value at a time', async () => {
    await TestBed.configureTestingModule({
      imports: [NetWorthChart],
      providers: [provideI18nTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(NetWorthChart);
    fixture.componentRef.setInput('points', yearPoints);
    fixture.componentRef.setInput('range', '1Y');
    fixture.componentRef.setInput('classes', CLASSES);
    fixture.componentRef.setInput('groups', [
      { key: 'g-1', label: 'Emerytura' },
      { key: 'ungrouped', label: 'Bez grupy' },
    ]);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;
    const chart = fixture.componentInstance;
    const label = () => textOf(element.querySelector('.net-worth-chart__filter'));

    expect(element.querySelector('mat-chip-listbox')).toBeNull();
    expect(label()).toContain('All');
    const items = await openFilterMenu(fixture);
    expect(items.map((item) => textOf(item))).toEqual([
      'All',
      'ETF',
      'Stock',
      'Cash',
      'Emerytura',
      'Bez grupy',
    ]);

    menuItem(items, 'ETF').click();
    await fixture.whenStable();
    expect(chart.assetClass()).toBe(ASSET_CLASS.Etf);
    expect(chart.group()).toBeNull();
    expect(label()).toContain('ETF');

    menuItem(await openFilterMenu(fixture), 'Emerytura').click();
    await fixture.whenStable();
    expect(chart.group()).toBe('g-1');
    expect(chart.assetClass()).toBeNull();
    expect(label()).toContain('Emerytura');

    menuItem(await openFilterMenu(fixture), 'Bez grupy').click();
    await fixture.whenStable();
    expect(chart.group()).toBe('ungrouped');

    menuItem(await openFilterMenu(fixture), 'All').click();
    await fixture.whenStable();
    expect(chart.group()).toBeNull();
    expect(chart.assetClass()).toBeNull();
    expect(label()).toContain('All');
  });

  it('draws one band for a run of incomplete points', async () => {
    const element = await setup(dailyPoints('2026-08', 10, (i) => i >= 2 && i <= 5));

    const bands = bandsOf(element);

    expect(bands).toHaveLength(1);
    expect(Number(bands[0].getAttribute('x'))).toBeCloseTo(PLOT_LEFT + (2 / 9) * PLOT_WIDTH, 0);
    expect(Number(bands[0].getAttribute('width'))).toBeCloseTo((3 / 9) * PLOT_WIDTH, 0);
    expect(bands[0].getAttribute('aria-hidden')).toBe('true');
  });

  it('draws a band per run', async () => {
    const element = await setup(dailyPoints('2026-08', 20, (i) => i <= 8 || i >= 11));

    expect(bandsOf(element)).toHaveLength(2);
    expect(element.querySelectorAll('.net-worth-chart__band-label')).toHaveLength(2);
  });

  it('labels the band with the last incomplete day', async () => {
    const element = await setup(dailyPoints('2024-03', 20, (i) => i <= 11));
    const label = () => normalised(textOf(element.querySelector('.net-worth-chart__band-label')));

    expect(label()).toMatch(/incomplete/i);
    expect(label()).toContain(formatDate('2024-03-12'));

    await switchLanguage(fixture, 'pl');

    expect(label()).toBe(`wycena niepełna do ${normalised(formatDate('2024-03-12'))}`);
  });

  it('draws no band when every point is complete', async () => {
    const element = await setup(dailyPoints('2026-08', 10, () => false));

    expect(bandsOf(element)).toHaveLength(0);
    expect(element.querySelector('.net-worth-chart__band-label')).toBeNull();
  });

  it('marks an incomplete point in the tooltip', async () => {
    const element = await setup(dailyPoints('2026-08', 10, (i) => i <= 4));

    hoverPlot(element, 0);
    await fixture.whenStable();

    expect(textOf(element.querySelector('.net-worth-chart__tooltip'))).toMatch(/incomplete/i);

    hoverPlot(element, 300);
    await fixture.whenStable();

    expect(textOf(element.querySelector('.net-worth-chart__tooltip'))).not.toMatch(/incomplete/i);
  });

  it('clips the band to the visible range', async () => {
    const element = await setup(dailyPoints('2024-03', 20, (i) => i <= 11));
    const label = () => normalised(textOf(element.querySelector('.net-worth-chart__band-label')));
    const before = label();

    fixture.componentRef.setInput('points', dailyPoints('2024-03', 20, (i) => i <= 11).slice(5));
    fixture.componentRef.setInput('range', '1M');
    await fixture.whenStable();

    const bands = bandsOf(element);
    expect(bands).toHaveLength(1);
    expect(Number(bands[0].getAttribute('x'))).toBeCloseTo(PLOT_LEFT, 0);
    expect(Number(bands[0].getAttribute('width'))).toBeCloseTo((6 / 14) * PLOT_WIDTH, 0);
    expect(label()).toBe(before);
    expect(label()).toContain(formatDate('2024-03-12'));
  });

  it('draws in the measured container size', async () => {
    const observers = stubResizeObserver();
    const element = await setup(yearPoints);

    await resizeTo(observers, 800, 420);

    const plot = element.querySelector('.net-worth-chart__plot') as SVGElement;
    expect(element.querySelector('svg')?.getAttribute('viewBox')).toBe('0 0 800 420');
    expect(Number(plot.getAttribute('width'))).toBe(800 - 64 - 16);
    expect(Number(plot.getAttribute('height'))).toBe(420 - 12 - 28);
  });

  it('falls back to 640 by 280 without a measurement', async () => {
    vi.stubGlobal('ResizeObserver', undefined);
    const element = await setup(yearPoints);

    expect(element.querySelector('svg')?.getAttribute('viewBox')).toBe('0 0 640 280');
  });

  it('falls back to 640 by 280 when the measured size is 0', async () => {
    const observers = stubResizeObserver();
    const element = await setup(yearPoints);
    await resizeTo(observers, 800, 420);
    expect(element.querySelector('svg')?.getAttribute('viewBox')).toBe('0 0 800 420');

    await resizeTo(observers, 0, 0);

    expect(element.querySelector('svg')?.getAttribute('viewBox')).toBe('0 0 640 280');
    const plot = element.querySelector('.net-worth-chart__plot') as SVGElement;
    expect(Number(plot.getAttribute('width'))).toBe(PLOT_WIDTH);
  });

  it('redraws when the container is resized', async () => {
    const observers = stubResizeObserver();
    const element = await setup(yearPoints);
    await resizeTo(observers, 800, 420);
    const line = () => element.querySelector('path.net-worth-chart__line')?.getAttribute('d');
    const xPositions = () =>
      [...element.querySelectorAll('.net-worth-chart__x-label')].map((l) => l.getAttribute('x'));
    const yPositions = () =>
      [...element.querySelectorAll('.net-worth-chart__y-label')].map((l) => l.getAttribute('y'));
    const before = { line: line(), x: xPositions(), y: yPositions() };

    await resizeTo(observers, 400, 200);

    expect(element.querySelector('svg')?.getAttribute('viewBox')).toBe('0 0 400 200');
    expect(line()).not.toBe(before.line);
    expect(xPositions()).not.toEqual(before.x);
    expect(yPositions()).not.toEqual(before.y);
  });

  it('maps the pointer to the nearest point at a measured size', async () => {
    const observers = stubResizeObserver();
    const element = await setup(yearPoints);
    await resizeTo(observers, 800, 420);

    hoverPlot(element, 150);
    await fixture.whenStable();

    expect(textOf(element.querySelector('.net-worth-chart__tooltip'))).toContain(
      formatDate('2026-07-01'),
    );

    hoverPlot(element, 300);
    await fixture.whenStable();

    expect(textOf(element.querySelector('.net-worth-chart__tooltip'))).toContain(
      formatDate('2026-08-01'),
    );
    expect(Number(element.querySelector('.net-worth-chart__marker')?.getAttribute('cx'))).toBe(
      800 - 16,
    );

    hoverPlot(element, 0);
    await fixture.whenStable();

    expect(Number(element.querySelector('.net-worth-chart__marker')?.getAttribute('cx'))).toBe(64);
  });

  it('keeps the 640 by 280 proportion and grows to fill', async () => {
    await TestBed.configureTestingModule({ providers: [provideI18nTesting()] }).compileComponents();
    const scss = stylesheet();
    const host = ruleOf(scss, ':host');
    const canvas = ruleOf(scss, '.net-worth-chart__canvas');

    expect(host).toMatch(/display:\s*flex/);
    expect(host).toMatch(/flex-direction:\s*column/);
    expect(host).toMatch(/flex:\s*1/);
    expect(canvas).toMatch(/aspect-ratio:\s*640\s*\/\s*280/);
    expect(canvas).toMatch(/flex:\s*1/);
  });

  it('centres the loading, error and empty states', async () => {
    const element = await setup([{ date: '2026-08-01', netWorthPln: 10000, isIncomplete: false }]);
    expect(element.querySelector('.net-worth-chart__empty')).not.toBeNull();

    fixture.componentRef.setInput('loadError', 'Could not load history.');
    await fixture.whenStable();
    expect(element.querySelector('.net-worth-chart__state app-section-error')).not.toBeNull();

    fixture.componentRef.setInput('loadError', null);
    fixture.componentRef.setInput('loading', true);
    await fixture.whenStable();
    expect(element.querySelector('.net-worth-chart__state mat-spinner')).not.toBeNull();

    const scss = stylesheet();
    const state = ruleOf(scss, '.net-worth-chart__state');
    const empty = ruleOf(scss, '.net-worth-chart__empty');

    expect(state).toMatch(/flex:\s*1/);
    expect(state).toMatch(/justify-content:\s*center/);
    expect(state).toMatch(/align-items:\s*center/);
    expect(empty).toMatch(/flex:\s*1/);
    expect(empty).toMatch(/(display:\s*flex|text-align:\s*center)/);
  });

  it('shows not enough history', async () => {
    const element = await setup(
      [{ date: '2026-08-01', netWorthPln: 10000, isIncomplete: false }],
      '1Y',
      CLASSES,
    );

    expect(textOf(element.querySelector('.net-worth-chart__empty'))).toBe(
      'Not enough history to chart yet.',
    );
    expect(element.querySelector('mat-button-toggle-group')).not.toBeNull();
    expect(element.querySelector('.net-worth-chart__filter')).not.toBeNull();
  });

  function chartElement(element: HTMLElement): SVGElement {
    return element.querySelector('svg.net-worth-chart__svg') as SVGElement;
  }

  async function pressKey(element: HTMLElement, key: string): Promise<void> {
    chartElement(element).dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true }));
    await fixture.whenStable();
  }

  async function blurChart(element: HTMLElement): Promise<void> {
    const chart = chartElement(element);
    chart.dispatchEvent(new FocusEvent('blur'));
    chart.dispatchEvent(new FocusEvent('focusout', { bubbles: true }));
    await fixture.whenStable();
  }

  function tooltipText(element: HTMLElement): string {
    return normalised(textOf(element.querySelector('.net-worth-chart__tooltip')));
  }

  function liveText(element: HTMLElement): string {
    return normalised(textOf(element.querySelector('[aria-live="polite"]')));
  }

  it('draws only the line without an area fill', async () => {
    const element = await setup(yearPoints);

    const areaClass = ['net-worth-chart', 'area'].join('__');
    expect(element.querySelector(`.${areaClass}`)).toBeNull();
    expect(stylesheet()).not.toContain(areaClass);
    const line = element.querySelector('.net-worth-chart__line');
    expect(line).not.toBeNull();
    expect(line?.getAttribute('d')).toBeTruthy();
  });

  it('keeps the y axis fitted to the data', async () => {
    const element = await setup(yearPoints);

    const expected = niceTicks(10000, 11000, 5);

    expect(element.querySelectorAll('.net-worth-chart__gridline').length).toBe(expected.length);
    expect(element.querySelectorAll('.net-worth-chart__y-label').length).toBe(expected.length);
  });

  it('is a keyboard stop with a visible focus outline', async () => {
    const element = await setup(yearPoints);

    const chart = chartElement(element);
    expect(chart.getAttribute('tabindex')).toBe('0');
    expect(chart.getAttribute('aria-label')).toBeTruthy();
    expect(ruleOf(stylesheet(), '.net-worth-chart__svg:focus-visible')).toMatch(/outline:/);
  });

  it('moves the point with the arrow keys and stops at the ends', async () => {
    const element = await setup(yearPoints);

    await pressKey(element, 'ArrowLeft');
    expect(tooltipText(element)).toContain(formatDate('2026-08-01'));

    await pressKey(element, 'ArrowLeft');
    expect(tooltipText(element)).toContain(formatDate('2026-07-01'));

    await pressKey(element, 'ArrowLeft');
    expect(tooltipText(element)).toContain(formatDate('2026-06-01'));

    await pressKey(element, 'ArrowLeft');
    expect(tooltipText(element)).toContain(formatDate('2026-06-01'));

    await pressKey(element, 'ArrowRight');
    expect(tooltipText(element)).toContain(formatDate('2026-07-01'));

    await pressKey(element, 'ArrowRight');
    await pressKey(element, 'ArrowRight');
    expect(tooltipText(element)).toContain(formatDate('2026-08-01'));
  });

  it('selects the last point on the first arrow key', async () => {
    const left = await setup(yearPoints);
    expect(left.querySelector('.net-worth-chart__tooltip')).toBeNull();
    await pressKey(left, 'ArrowLeft');
    expect(tooltipText(left)).toContain(formatDate('2026-08-01'));
    fixture.destroy();
    TestBed.resetTestingModule();

    const right = await setup(yearPoints);
    await pressKey(right, 'ArrowRight');
    expect(tooltipText(right)).toContain(formatDate('2026-08-01'));
  });

  it('jumps to the first and last point with Home and End', async () => {
    const element = await setup(yearPoints);

    await pressKey(element, 'Home');
    expect(tooltipText(element)).toContain(formatDate('2026-06-01'));

    await pressKey(element, 'End');
    expect(tooltipText(element)).toContain(formatDate('2026-08-01'));
  });

  it('hides the tooltip on Escape and on blur', async () => {
    const element = await setup(yearPoints);

    await pressKey(element, 'Home');
    expect(element.querySelector('.net-worth-chart__tooltip')).not.toBeNull();
    expect(liveText(element)).not.toBe('');

    await pressKey(element, 'Escape');
    expect(element.querySelector('.net-worth-chart__tooltip')).toBeNull();
    expect(element.querySelector('.net-worth-chart__guide')).toBeNull();
    expect(element.querySelector('.net-worth-chart__marker')).toBeNull();
    expect(liveText(element)).toBe('');

    await pressKey(element, 'End');
    expect(element.querySelector('.net-worth-chart__tooltip')).not.toBeNull();

    await blurChart(element);
    expect(element.querySelector('.net-worth-chart__tooltip')).toBeNull();
    expect(liveText(element)).toBe('');
  });

  it('shows the same tooltip for keyboard and pointer', async () => {
    const element = await setup(yearPoints);

    hoverPlot(element, 150);
    await fixture.whenStable();
    const pointer = tooltipText(element);
    const pointerClass = element.querySelector('.net-worth-chart__tooltip')?.className;
    element
      .querySelector('.net-worth-chart__plot')
      ?.dispatchEvent(new MouseEvent('pointerleave', { bubbles: true }));
    await fixture.whenStable();

    await pressKey(element, 'Home');
    await pressKey(element, 'ArrowRight');

    expect(tooltipText(element)).toBe(pointer);
    expect(element.querySelector('.net-worth-chart__tooltip')?.className).toBe(pointerClass);
    expect(element.querySelector('.net-worth-chart__guide')).not.toBeNull();
    expect(element.querySelector('.net-worth-chart__marker')).not.toBeNull();
  });

  it('announces the selected point in a polite live region', async () => {
    const element = await setup(yearPoints);

    await pressKey(element, 'Home');

    expect(liveText(element)).toBe(
      normalised(`${formatDate('2026-06-01')}: ${formatMoney(10000)}`),
    );

    await pressKey(element, 'End');

    expect(liveText(element)).toBe(
      normalised(`${formatDate('2026-08-01')}: ${formatMoney(10500)}`),
    );

    fixture.destroy();
    TestBed.resetTestingModule();
    const incomplete = await setup(dailyPoints('2026-08', 3, (i) => i === 2));
    await pressKey(incomplete, 'End');
    const note = textOf(incomplete.querySelector('.net-worth-chart__tooltip-incomplete'));
    expect(note).not.toBe('');
    expect(liveText(incomplete)).toContain(normalised(note));
    expect(liveText(incomplete)).toContain(`${formatDate('2026-08-03')}: `);
  });

  it('does not announce on pointer moves', async () => {
    const element = await setup(yearPoints);
    expect(liveText(element)).toBe('');

    hoverPlot(element, 0);
    await fixture.whenStable();
    expect(liveText(element)).toBe('');

    await pressKey(element, 'End');
    const announced = liveText(element);

    hoverPlot(element, 150);
    await fixture.whenStable();
    expect(liveText(element)).toBe(announced);
  });

  it('has no keyboard stop or live region with fewer than two points', async () => {
    const element = await setup([]);

    for (const points of [[], yearPoints.slice(0, 1)]) {
      fixture.componentRef.setInput('points', points);
      await fixture.whenStable();

      expect(element.querySelector('.net-worth-chart__empty')).not.toBeNull();
      expect(element.querySelector('svg')).toBeNull();
      expect(element.querySelector('[aria-live]')).toBeNull();
    }
  });
});
