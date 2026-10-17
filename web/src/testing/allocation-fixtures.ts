export const BUCKET_KIND = {
  Investments: 0,
  Savings: 1,
  Cushion: 2,
  Ike: 3,
  Ikze: 4,
  IkzeJdg: 5,
} as const;
export const SHARE_TYPE = { Fixed: 0, Percent: 1 } as const;

export function allocationBucket(
  name: string,
  extra: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    bucketId: `bucket-${name}`,
    name,
    kind: BUCKET_KIND.Investments,
    shareType: SHARE_TYPE.Percent,
    share: 50,
    position: 0,
    isRemainder: false,
    accountId: null,
    accountName: null,
    allocatedPln: 0,
    underfunded: false,
    ...extra,
  };
}

export function cushionBucket(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return allocationBucket('Poduszka', {
    kind: BUCKET_KIND.Cushion,
    shareType: null,
    share: null,
    position: 0,
    allocatedPln: 5200,
    cushion: {
      balanceStartPln: 20000,
      targetPln: 25200,
      targetMonths: 6,
      balanceEndPln: 25200,
      reached: false,
    },
    ...extra,
  });
}

export function limitedBucket(
  name: string,
  extra: Record<string, unknown> = {},
): Record<string, unknown> {
  return allocationBucket(name, {
    kind: BUCKET_KIND.Ike,
    allocatedPln: 2260,
    limitReached: true,
    limit: {
      year: 2026,
      limitPln: 28260,
      contributedStartPln: 26000,
      contributedEndPln: 28260,
      reached: true,
      estimated: false,
    },
    ...extra,
  });
}

export function allocation(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    effectiveFrom: null,
    nextChange: null,
    forecastPln: 0,
    incomplete: false,
    deficitPln: 0,
    buckets: [],
    toInvestPln: 0,
    ...extra,
  };
}

export function transferRow(
  bucketName: string,
  amountPln: number,
  extra: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    id: null,
    batchNumber: null,
    checked: null,
    bucketId: `bucket-${bucketName}`,
    bucketName,
    accountId: null,
    accountName: null,
    groupName: null,
    amountPln,
    marker: 'None',
    ...extra,
  };
}

export function transferGroup(
  name: string,
  currentPercent: number,
  afterPercent: number,
  targetPercent: number,
): Record<string, unknown> {
  return { name, currentPercent, afterPercent, targetPercent };
}

export function transferPlan(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    status: 'Preview',
    noneReason: null,
    minimumTransferPln: 100,
    valuationDate: '2026-10-14',
    valuationStale: false,
    fallbackReason: null,
    rows: [],
    groups: [],
    toInvestPln: 0,
    canUndo: false,
    ...extra,
  };
}

export function firstPlanBuckets(): Record<string, unknown>[] {
  return [
    allocationBucket('Wakacje', {
      kind: BUCKET_KIND.Savings,
      shareType: SHARE_TYPE.Fixed,
      share: 500,
      position: 0,
      allocatedPln: 500,
    }),
    allocationBucket('Akcje', { share: 50, position: 0, allocatedPln: 5871 }),
    allocationBucket('Lokata', {
      kind: BUCKET_KIND.Savings,
      share: 20,
      position: 1,
      allocatedPln: 2348,
    }),
    allocationBucket('ETF', { share: 30, position: 2, isRemainder: true, allocatedPln: 3524.27 }),
  ];
}
