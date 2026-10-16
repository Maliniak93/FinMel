export const BUCKET_KIND = { Investments: 0, Savings: 1 } as const;
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
