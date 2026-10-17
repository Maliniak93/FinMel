import { vi } from 'vitest';

import type {
  SaveTargetAllocationRequest,
  TargetAllocationGroupResponse,
  TargetAllocationResponse,
} from '../../../api/reporting';
import { ASSET_CLASS } from '../../assets/asset-class';
import { jsonResponse, requestUrl } from '../../dashboard/testing/dashboard-fixtures';

export const TARGET_ALLOCATION_PATH = '/api/reporting/target-allocation';

export const byAssetClass: TargetAllocationResponse['byAssetClass'] = [
  { assetClass: ASSET_CLASS.Cash, valuePln: 12000 },
  { assetClass: ASSET_CLASS.Deposit, valuePln: 20000 },
  { assetClass: ASSET_CLASS.Stock, valuePln: 70000 },
  { assetClass: ASSET_CLASS.Etf, valuePln: 40000 },
  { assetClass: ASSET_CLASS.Bond, valuePln: 40000 },
  { assetClass: ASSET_CLASS.PreciousMetal, valuePln: 10000 },
  { assetClass: ASSET_CLASS.BankAccount, valuePln: 8000 },
];

export const savedGroups: TargetAllocationGroupResponse[] = [
  {
    name: 'Akcje/ETF',
    assetClasses: [ASSET_CLASS.Stock, ASSET_CLASS.Etf],
    targetPercent: 50,
    bandPp: 5,
    bandLowPercent: 45,
    bandHighPercent: 55,
    valuePln: 110000,
    currentPercent: 61.1,
    deviationPp: 11.1,
    targetValuePln: 90000,
    deviationPln: 20000,
    isInBand: false,
  },
  {
    name: 'Obligacje',
    assetClasses: [ASSET_CLASS.Bond, ASSET_CLASS.Deposit],
    targetPercent: 40,
    bandPp: 5,
    bandLowPercent: 35,
    bandHighPercent: 45,
    valuePln: 60000,
    currentPercent: 33.3,
    deviationPp: -6.7,
    targetValuePln: 72000,
    deviationPln: -12000,
    isInBand: false,
  },
  {
    name: 'Kruszce',
    assetClasses: [ASSET_CLASS.PreciousMetal],
    targetPercent: 10,
    bandPp: 5,
    bandLowPercent: 5,
    bandHighPercent: 15,
    valuePln: 10000,
    currentPercent: 5.6,
    deviationPp: -4.4,
    targetValuePln: 18000,
    deviationPln: -8000,
    isInBand: true,
  },
];

export const configuredAllocation: TargetAllocationResponse = {
  isConfigured: true,
  asOf: '2026-08-09',
  isStale: false,
  totalPln: 200000,
  basePln: 180000,
  outsidePln: 20000,
  outsideAssetClasses: [ASSET_CLASS.Cash, ASSET_CLASS.BankAccount],
  byAssetClass,
  groups: savedGroups,
};

export const notConfiguredAllocation: TargetAllocationResponse = {
  isConfigured: false,
  asOf: '2026-08-09',
  isStale: false,
  totalPln: 200000,
  basePln: 0,
  outsidePln: 0,
  outsideAssetClasses: [],
  byAssetClass,
  groups: [],
};

export function zeroBaseAllocation(
  overrides: Partial<TargetAllocationResponse> = {},
): TargetAllocationResponse {
  return {
    ...configuredAllocation,
    basePln: 0,
    outsidePln: 0,
    outsideAssetClasses: [],
    byAssetClass: [],
    groups: configuredAllocation.groups.map((group) => ({
      ...group,
      valuePln: 0,
      currentPercent: null,
      deviationPp: null,
      targetValuePln: null,
      deviationPln: null,
      isInBand: null,
    })),
    ...overrides,
  };
}

export function savedBody(request: SaveTargetAllocationRequest) {
  return request.groups.map((group) => ({
    name: group.name,
    assetClasses: group.assetClasses.map(Number).sort((a, b) => a - b),
    targetPercent: Number(group.targetPercent),
    bandPp: Number(group.bandPp),
  }));
}

export interface TargetAllocationApi {
  fetchSpy: ReturnType<typeof vi.spyOn>;
  puts: SaveTargetAllocationRequest[];
  gets: () => number;
}

// get and put build a fresh Response per call (a body is read once); put receives the 1-based attempt number.
export function mockTargetAllocationApi(options: {
  get: (call: number) => Response;
  put?: (attempt: number) => Response;
}): TargetAllocationApi {
  const puts: SaveTargetAllocationRequest[] = [];
  let getCalls = 0;
  const fetchSpy = vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = requestUrl(input);
    if (!url.includes(TARGET_ALLOCATION_PATH)) {
      throw new Error(`Unexpected request: ${url}`);
    }
    const request = input as Request;
    if (request.method === 'PUT') {
      puts.push((await request.clone().json()) as SaveTargetAllocationRequest);
      return (options.put ?? (() => jsonResponse(configuredAllocation)))(puts.length);
    }
    getCalls += 1;
    return options.get(getCalls);
  });
  return { fetchSpy, puts, gets: () => getCalls };
}
