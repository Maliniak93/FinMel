import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PieChart, type PieChartSegment } from './pie-chart';
import { provideI18nTesting } from '../../core/i18n/testing';

describe('PieChart', () => {
  let fixture: ComponentFixture<PieChart>;
  let component: PieChart;

  async function setup(segments: readonly PieChartSegment[], selected?: string): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [PieChart],
      providers: [provideI18nTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(PieChart);
    fixture.componentRef.setInput('segments', segments);
    if (selected !== undefined) {
      fixture.componentRef.setInput('selected', selected);
    }
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

  function arcs(): SVGElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('circle'));
  }

  const keyedSegments: readonly PieChartSegment[] = [
    { key: '0', label: 'Cash', percentage: 30, color: '#111' },
    { key: '3', label: 'ETF', percentage: 70, color: '#222' },
  ];

  it('should create', async () => {
    await setup([]);
    expect(component).toBeTruthy();
  });

  it('renders no arcs and an empty message when there are no segments', async () => {
    await setup([]);

    expect(component['rendered']()).toEqual([]);
    expect(fixture.nativeElement.textContent).toContain('No data to chart yet.');
  });

  it('drops zero-percentage segments but keeps their position out of the cumulative offset', async () => {
    await setup([
      { key: '0', label: 'Cash', percentage: 0, color: '#111' },
      { key: '2', label: 'Stock', percentage: 40, color: '#222' },
    ]);

    const rendered = component['rendered']();
    expect(rendered).toHaveLength(1);
    expect(rendered[0].label).toBe('Stock');
    expect(rendered[0].dashOffset).toBe(-0);
  });

  it('accumulates dash offsets across segments in order', async () => {
    await setup([
      { key: '0', label: 'Cash', percentage: 30, color: '#111' },
      { key: '2', label: 'Stock', percentage: 20, color: '#222' },
      { key: '4', label: 'Bond', percentage: 50, color: '#333' },
    ]);

    const rendered = component['rendered']();
    expect(rendered.map((s) => s.dashOffset)).toEqual([-0, -30, -50]);
    expect(rendered.map((s) => s.dashArray)).toEqual(['30 70', '20 80', '50 50']);
  });

  it('dims segments other than the selected one', async () => {
    await setup(keyedSegments, '3');

    expect(arcs().map((arc) => arc.classList.contains('pie-chart__segment--dimmed'))).toEqual([
      true,
      false,
    ]);
  });

  it('emits the clicked segment key', async () => {
    await setup(keyedSegments);
    const emitted: string[] = [];
    component.segmentClick.subscribe((key) => emitted.push(key));

    arcs()[1].dispatchEvent(new MouseEvent('click', { bubbles: true }));

    expect(emitted).toEqual(['3']);
  });
});
