import type { AssetGroupResponse, AssignableAssetResponse } from '../../api/portfolio';
import type { DashboardResponse } from '../../api/reporting';
import { buildGroupMetrics, buildGroupsSummary } from './group-metrics';
import { blueGroup, blueGroupId, greenGroupId } from './testing/group-fixtures';

function dashboard(overrides: Partial<DashboardResponse> = {}): DashboardResponse {
  return {
    netWorthPln: 0,
    asOf: '2026-10-24',
    isStale: false,
    byAssetClass: [],
    byAccount: [],
    byGroup: [],
    byAsset: [],
    ...overrides,
  };
}

function group(overrides: Partial<AssetGroupResponse> = {}): AssetGroupResponse {
  return { ...blueGroup, ...overrides };
}

function asset(
  id: string,
  overrides: Partial<AssignableAssetResponse> = {},
): AssignableAssetResponse {
  return {
    assetId: id,
    name: id,
    assetClass: 2,
    currency: 'PLN',
    accountId: null,
    accountName: null,
    cashPlaceName: null,
    groupId: blueGroupId,
    isArchived: false,
    ...overrides,
  };
}

function valued(groupValue: number, byAssetClass: { assetClass: number; valuePln: number }[] = []) {
  return dashboard({
    byGroup: [{ groupId: blueGroupId, valuePln: groupValue, percentage: 100, byAssetClass }],
  });
}

describe('group metrics', () => {
  it('progress states', () => {
    const target = group({ targetPln: 1000 });

    const below = buildGroupMetrics(target, valued(250), []);
    expect(below.value).toBe(250);
    expect(below.progress.state).toBe('below');
    expect(below.progress.ratio).toBeCloseTo(0.25);
    expect(below.progress.shortfall).toBe(750);

    const equal = buildGroupMetrics(target, valued(1000), []);
    expect(equal.progress.state).toBe('reached');
    expect(equal.progress.ratio).toBe(1);

    const above = buildGroupMetrics(target, valued(1500), []);
    expect(above.progress.state).toBe('reached');
    expect(above.progress.ratio).toBe(1);

    const zero = buildGroupMetrics(target, valued(0), []);
    expect(zero.progress.state).toBe('below');
    expect(zero.progress.ratio).toBe(0);
    expect(zero.progress.shortfall).toBe(1000);

    const noTarget = buildGroupMetrics(group({ targetPln: null }), valued(500), []);
    expect(noTarget.progress.state).toBe('none');
  });

  it('class rows', () => {
    const withAllocation = group({
      classAllocation: [
        { assetClass: 2, percent: 50 },
        { assetClass: 4, percent: 50 },
      ],
    });
    const metrics = buildGroupMetrics(
      withAllocation,
      valued(1000, [
        { assetClass: 2, valuePln: 800 },
        { assetClass: 0, valuePln: 200 },
      ]),
      [],
    );

    const row = (assetClass: number) => metrics.classRows.find((r) => r.assetClass === assetClass)!;
    expect(metrics.classRows).toHaveLength(3);
    expect(row(2)).toMatchObject({ target: 50, actual: 80, deviation: 30, flagged: true });
    expect(row(4)).toMatchObject({ target: 50, actual: 0, deviation: -50, flagged: true });
    expect(row(0)).toMatchObject({ target: 0, actual: 20, deviation: 20, flagged: true });
    expect(metrics.maxDeviation).toBe(50);

    const edge = buildGroupMetrics(
      group({
        classAllocation: [
          { assetClass: 2, percent: 50 },
          { assetClass: 4, percent: 50 },
        ],
      }),
      valued(1000, [
        { assetClass: 2, valuePln: 550 },
        { assetClass: 4, valuePln: 450 },
      ]),
      [],
    );
    expect(edge.classRows.find((r) => r.assetClass === 2)).toMatchObject({
      deviation: 5,
      flagged: true,
    });
    const calm = buildGroupMetrics(
      group({ classAllocation: [{ assetClass: 2, percent: 50 }] }),
      valued(1000, [{ assetClass: 2, valuePln: 540 }]),
      [],
    );
    expect(calm.classRows[0]).toMatchObject({ deviation: 4, flagged: false });
  });

  it('empty group', () => {
    const metrics = buildGroupMetrics(
      group({ targetPln: 1000, classAllocation: [{ assetClass: 2, percent: 60 }] }),
      dashboard(),
      [],
    );

    expect(metrics.value).toBe(0);
    expect(metrics.classRows).toHaveLength(1);
    expect(metrics.classRows[0]).toMatchObject({ target: 60, actual: 0, deviation: -60 });
    expect(Number.isNaN(metrics.classRows[0].actual)).toBe(false);
    expect(Number.isNaN(metrics.classRows[0].deviation)).toBe(false);
    expect(Number.isNaN(metrics.progress.ratio)).toBe(false);
    expect(Number.isNaN(metrics.maxDeviation)).toBe(false);

    const noAllocation = buildGroupMetrics(group({ targetPln: null }), valued(0), []);
    expect(noAllocation.classRows).toEqual([]);
    expect(noAllocation.maxDeviation).toBe(0);
  });

  it('incomplete value', () => {
    const assets = [
      asset('no-row'),
      asset('stale'),
      asset('archived', { isArchived: true }),
      asset('valued'),
      asset('other-group', { groupId: greenGroupId }),
    ];
    const board = dashboard({
      byGroup: [{ groupId: blueGroupId, valuePln: 100, percentage: 100, byAssetClass: [] }],
      byAsset: [
        { assetId: 'stale', valuePln: 10, priceDate: '2026-09-01', isStale: true },
        { assetId: 'valued', valuePln: 90, priceDate: '2026-10-24', isStale: false },
      ],
    });

    expect(buildGroupMetrics(group(), board, assets).unvaluedCount).toBe(2);
  });

  it('is unavailable without a dashboard', () => {
    const metrics = buildGroupMetrics(group({ targetPln: 1000 }), null, [asset('a')]);

    expect(metrics.value).toBeNull();
  });

  it('summary', () => {
    const board = dashboard({
      netWorthPln: 1000,
      byGroup: [
        { groupId: blueGroupId, valuePln: 600, percentage: 60, byAssetClass: [] },
        { groupId: greenGroupId, valuePln: 100, percentage: 10, byAssetClass: [] },
        { groupId: null, valuePln: 300, percentage: 30, byAssetClass: [] },
      ],
    });
    const groups = [
      group({ monthlyContributionPln: 500 }),
      group({ id: greenGroupId, monthlyContributionPln: 250.5 }),
    ];
    const assets = [
      asset('free', { groupId: null }),
      asset('free-archived', { groupId: null, isArchived: true }),
      asset('member'),
    ];

    const summary = buildGroupsSummary(groups, board, assets);

    expect(summary.grouped).toBe(700);
    expect(summary.ungrouped).toBe(300);
    expect(summary.ungroupedCount).toBe(1);
    expect(summary.contributionTotal).toBe(750.5);

    const absent = buildGroupsSummary(groups, valued(600), assets);
    expect(absent.ungrouped).toBe(0);
  });
});
