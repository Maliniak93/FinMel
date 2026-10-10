import { ComponentFixture, TestBed } from '@angular/core/testing';

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
import { NetWorthChart } from './net-worth-chart';

function normalised(text: string): string {
  return text.replace(/\s+/g, ' ');
}

const ETF_COLOUR = /#82c91e|rgb\(130, 201, 30\)/i;
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
  { date: '2026-06-01', netWorthPln: 10000 },
  { date: '2026-07-01', netWorthPln: 11000 },
  { date: '2026-08-01', netWorthPln: 10500 },
];

const monthPoints: HistoryPoints = Array.from({ length: 20 }, (_, i) => ({
  date: `2026-08-${String(i + 1).padStart(2, '0')}`,
  netWorthPln: 10000 + i * 100,
}));

describe('NetWorthChart', () => {
  let fixture: ComponentFixture<NetWorthChart>;

  afterEach(async () => {
    await restoreEnglish();
  });

  async function setup(
    points: HistoryPoints,
    range = '1Y',
    classes?: readonly number[],
    assetClass?: number | null,
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
    await fixture.whenStable();
    return fixture.nativeElement as HTMLElement;
  }

  it('renders axes and gridlines', async () => {
    const element = await setup(yearPoints);

    const gridlines = element.querySelectorAll('.net-worth-chart__gridline');
    const yLabels = textsOf(element, '.net-worth-chart__y-label');
    const xLabels = textsOf(element, '.net-worth-chart__x-label');
    const area = element.querySelector('path.net-worth-chart__area');

    expect(gridlines.length).toBeGreaterThanOrEqual(4);
    expect(gridlines.length).toBeLessThanOrEqual(6);
    expect(yLabels).toHaveLength(gridlines.length);
    for (const label of yLabels) {
      expect(label).toMatch(/K/);
    }
    expect(xLabels.length).toBeGreaterThanOrEqual(1);
    expect(xLabels.length).toBeLessThanOrEqual(3);
    expect(area?.getAttribute('d')).toBeTruthy();
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
    expect(paintOf(element.querySelector('path.net-worth-chart__area'))).toMatch(ETF_COLOUR);
    expect(textOutsideControls(element)).toContain('ETF');
    expect(element.querySelector('svg')?.getAttribute('aria-label')).toContain('ETF');

    hoverPlot(element, 0);
    await fixture.whenStable();

    expect(textOf(element.querySelector('.net-worth-chart__tooltip'))).toMatch(/^ETF/);
  });

  it('shows no class label or colour without a selected class', async () => {
    const element = await setup(yearPoints, '1Y', CLASSES, null);

    expect(paintOf(element.querySelector('path.net-worth-chart__line'))).not.toMatch(ETF_COLOUR);
    expect(paintOf(element.querySelector('path.net-worth-chart__area'))).not.toMatch(ETF_COLOUR);
    expect(textOutsideControls(element)).not.toContain('ETF');
    expect(element.querySelector('svg')?.getAttribute('aria-label') ?? '').not.toContain('ETF');

    hoverPlot(element, 0);
    await fixture.whenStable();

    expect(textOf(element.querySelector('.net-worth-chart__tooltip'))).not.toContain('ETF');
  });

  it('not enough history', async () => {
    const element = await setup([{ date: '2026-08-01', netWorthPln: 10000 }]);

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
    expect(textsOf(element, 'mat-button-toggle')).toEqual(['1M', '1Y', 'YTD', 'MAX']);
  });
});
