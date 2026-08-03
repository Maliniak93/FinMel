import { ComponentFixture, TestBed } from '@angular/core/testing';

import { PieChart, type PieChartSegment } from './pie-chart';

describe('PieChart', () => {
  let fixture: ComponentFixture<PieChart>;
  let component: PieChart;

  async function setup(segments: readonly PieChartSegment[]): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [PieChart],
    }).compileComponents();

    fixture = TestBed.createComponent(PieChart);
    fixture.componentRef.setInput('segments', segments);
    component = fixture.componentInstance;
    await fixture.whenStable();
  }

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
      { label: 'Cash', percentage: 0, color: '#111' },
      { label: 'Stock', percentage: 40, color: '#222' },
    ]);

    const rendered = component['rendered']();
    expect(rendered).toHaveLength(1);
    expect(rendered[0].label).toBe('Stock');
    expect(rendered[0].dashOffset).toBe(-0);
  });

  it('accumulates dash offsets across segments in order', async () => {
    await setup([
      { label: 'Cash', percentage: 30, color: '#111' },
      { label: 'Stock', percentage: 20, color: '#222' },
      { label: 'Bond', percentage: 50, color: '#333' },
    ]);

    const rendered = component['rendered']();
    expect(rendered.map((s) => s.dashOffset)).toEqual([-0, -30, -50]);
    expect(rendered.map((s) => s.dashArray)).toEqual(['30 70', '20 80', '50 50']);
  });
});
