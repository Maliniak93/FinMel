import type { AssetGroupResponse, AssignableAssetResponse } from '../../api/portfolio';
import type { DashboardResponse } from '../../api/reporting';
import { ALLOCATION_DEVIATION_PP } from '../../shared/allocation-bar/allocation-bar';

export type GoalProgressState = 'none' | 'below' | 'reached';

export interface GroupProgress {
  state: GoalProgressState;
  ratio: number;
  shortfall: number;
}

export interface GroupClassRow {
  assetClass: number;
  target: number;
  actual: number;
  deviation: number;
  flagged: boolean;
}

export interface GroupMetrics {
  value: number | null;
  progress: GroupProgress;
  classRows: GroupClassRow[];
  maxDeviation: number;
  unvaluedCount: number;
}

export interface GroupsSummary {
  grouped: number | null;
  ungrouped: number | null;
  ungroupedCount: number;
  contributionTotal: number;
}

const toGrosze = (amount: number | string): number => Math.round(Number(amount) * 100);

function progressOf(targetPln: number | null, value: number | null): GroupProgress {
  if (targetPln === null || targetPln <= 0 || value === null) {
    return { state: 'none', ratio: 0, shortfall: 0 };
  }
  const target = toGrosze(targetPln);
  const held = toGrosze(value);
  return {
    state: held >= target ? 'reached' : 'below',
    ratio: Math.min(1, Math.max(0, held / target)),
    shortfall: Math.max(0, target - held) / 100,
  };
}

function classRowsOf(
  group: AssetGroupResponse,
  entry: DashboardResponse['byGroup'][number] | undefined,
): GroupClassRow[] {
  if (group.classAllocation.length === 0) {
    return [];
  }
  const groupGrosze = toGrosze(entry?.valuePln ?? 0);
  const held = new Map<number, number>();
  for (const part of entry?.byAssetClass ?? []) {
    held.set(
      Number(part.assetClass),
      (held.get(Number(part.assetClass)) ?? 0) + toGrosze(part.valuePln),
    );
  }
  const targets = group.classAllocation.map((share) => ({
    assetClass: Number(share.assetClass),
    percent: Number(share.percent),
  }));
  const extras = [...held]
    .filter(
      ([assetClass, grosze]) =>
        grosze > 0 && !targets.some((share) => share.assetClass === assetClass),
    )
    .map(([assetClass]) => ({ assetClass, percent: 0 }));

  return [...targets, ...extras].map((share) => {
    const actualTenths =
      groupGrosze > 0 ? Math.round(((held.get(share.assetClass) ?? 0) * 1000) / groupGrosze) : 0;
    const deviationTenths = actualTenths - Math.round(share.percent * 10);
    return {
      assetClass: share.assetClass,
      target: share.percent,
      actual: actualTenths / 10,
      deviation: deviationTenths / 10,
      flagged: Math.abs(deviationTenths) >= ALLOCATION_DEVIATION_PP * 10,
    };
  });
}

function unvaluedCountOf(
  group: AssetGroupResponse,
  dashboard: DashboardResponse,
  assets: readonly AssignableAssetResponse[],
): number {
  const lines = new Map(dashboard.byAsset.map((line) => [line.assetId, line]));
  return assets.filter((asset) => {
    if (asset.groupId !== group.id || asset.isArchived) {
      return false;
    }
    const line = lines.get(asset.assetId);
    return line === undefined || line.isStale;
  }).length;
}

export function buildGroupMetrics(
  group: AssetGroupResponse,
  dashboard: DashboardResponse | null,
  assets: readonly AssignableAssetResponse[],
): GroupMetrics {
  const targetPln =
    group.targetPln === null || group.targetPln === undefined ? null : Number(group.targetPln);
  if (dashboard === null) {
    return {
      value: null,
      progress: progressOf(targetPln, null),
      classRows: [],
      maxDeviation: 0,
      unvaluedCount: 0,
    };
  }

  const entry = dashboard.byGroup.find((candidate) => candidate.groupId === group.id);
  const value = entry ? Number(entry.valuePln) : 0;
  const classRows = classRowsOf(group, entry);
  return {
    value,
    progress: progressOf(targetPln, value),
    classRows,
    maxDeviation: classRows.reduce((max, row) => Math.max(max, Math.abs(row.deviation)), 0),
    unvaluedCount: unvaluedCountOf(group, dashboard, assets),
  };
}

export function buildGroupsSummary(
  groups: readonly AssetGroupResponse[],
  dashboard: DashboardResponse | null,
  assets: readonly AssignableAssetResponse[],
): GroupsSummary {
  const known = new Set(groups.map((group) => group.id));
  const contributionTotal =
    groups.reduce((sum, group) => sum + toGrosze(group.monthlyContributionPln), 0) / 100;
  const ungroupedCount = assets.filter(
    (asset) => !asset.isArchived && (!asset.groupId || !known.has(asset.groupId)),
  ).length;

  if (dashboard === null) {
    return { grouped: null, ungrouped: null, ungroupedCount, contributionTotal };
  }

  let grouped = 0;
  let ungrouped = 0;
  for (const entry of dashboard.byGroup) {
    if (entry.groupId && known.has(entry.groupId)) {
      grouped += toGrosze(entry.valuePln);
    } else {
      ungrouped += toGrosze(entry.valuePln);
    }
  }
  return { grouped: grouped / 100, ungrouped: ungrouped / 100, ungroupedCount, contributionTotal };
}
