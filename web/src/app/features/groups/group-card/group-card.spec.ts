import { ComponentFixture, TestBed } from '@angular/core/testing';

import type { AssetGroupResponse } from '../../../api/portfolio';
import type { DashboardResponse } from '../../../api/reporting';
import { provideI18nTesting } from '../../../core/i18n/testing';
import { formatMoney } from '../../../shared/format';
import { buildGroupMetrics } from '../group-metrics';
import { GroupCard } from './group-card';

const cushion: AssetGroupResponse = {
  id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  name: 'Poduszka finansowa',
  color: 'green',
  targetPln: 80000,
  assetCount: 2,
  kind: 1,
  monthlyContributionPln: 1000,
  sharePercent: null,
  classAllocation: [
    { assetClass: 1, percent: 70 },
    { assetClass: 9, percent: 30 },
  ],
};

const dashboard: DashboardResponse = {
  netWorthPln: 60000,
  asOf: '2026-10-24',
  isStale: false,
  byAssetClass: [],
  byAccount: [],
  byGroup: [
    {
      groupId: cushion.id,
      valuePln: 60000,
      percentage: 100,
      byAssetClass: [
        { assetClass: 1, valuePln: 50000 },
        { assetClass: 9, valuePln: 10000 },
      ],
    },
  ],
  byAsset: [],
};

describe('GroupCard', () => {
  let fixture: ComponentFixture<GroupCard>;

  function byTestId(id: string): HTMLElement {
    const found = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(
      `[data-testid="${id}"]`,
    );
    if (!found) {
      throw new Error(`No [data-testid=${id}].`);
    }
    return found;
  }

  async function render(group: AssetGroupResponse): Promise<void> {
    await TestBed.configureTestingModule({
      imports: [GroupCard],
      providers: [provideI18nTesting()],
    }).compileComponents();
    fixture = TestBed.createComponent(GroupCard);
    fixture.componentRef.setInput('group', group);
    fixture.componentRef.setInput('metrics', buildGroupMetrics(group, dashboard, []));
    fixture.componentRef.setInput('selected', false);
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('renders a cushion', async () => {
    await render(cushion);

    const root = fixture.nativeElement as HTMLElement;
    expect(byTestId('group-name').textContent).toContain('Poduszka finansowa');
    expect(byTestId('group-kind').querySelector('mat-icon')?.textContent?.trim()).toBe('shield');
    expect(byTestId('group-value').textContent).toContain(formatMoney(60000));
    expect(byTestId('group-value').textContent).toContain(formatMoney(80000));
    expect(root.querySelector('[role="progressbar"]')?.getAttribute('aria-valuenow')).toBe('75');
    expect(byTestId('group-progress-caption').textContent).toContain(formatMoney(20000));
    expect(byTestId('group-contribution').textContent).toContain(formatMoney(1000));
    expect(byTestId('group-count').textContent).toContain('2');
    expect(root.querySelectorAll('.allocation-bar__legend-row')).toHaveLength(2);
    const flag = byTestId('group-deviation-flag');
    expect(flag.textContent).toContain('13.3');
    expect(flag.querySelector('mat-icon')).toBeTruthy();
  });
});
