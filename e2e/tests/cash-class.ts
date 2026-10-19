import type { APIRequestContext } from '@playwright/test';
import { expect } from './fixtures';
import { expectProjected } from './projection';

export const ASSET_CLASS = { cash: 0, stock: 2, etf: 3, bond: 4 } as const;
export const ASSET_CLASS_DEPOSIT = 1;
export const ASSET_CLASS_BANK_ACCOUNT = 9;

export interface HistoryPoint {
  date: string;
  value: number;
}

function bearer(token: string): { Authorization: string } {
  return { Authorization: `Bearer ${token}` };
}

export async function fetchClassValues(
  request: APIRequestContext,
  token: string,
): Promise<Map<number, number>> {
  const response = await request.get('/api/reporting/dashboard', { headers: bearer(token) });
  expect(response.ok()).toBeTruthy();
  const body = (await response.json()) as {
    byAssetClass: { assetClass: number; valuePln: number }[];
  };
  return new Map(body.byAssetClass.map((entry) => [entry.assetClass, entry.valuePln]));
}

export async function expectClassesProjected(
  request: APIRequestContext,
  token: string,
  expected: Record<number, number>,
): Promise<void> {
  let current = new Map<number, number>();
  await expectProjected(
    async () => {
      current = await fetchClassValues(request, token);
    },
    async () => {
      for (const [assetClass, value] of Object.entries(expected)) {
        expect(current.get(Number(assetClass)) ?? 0, `class ${assetClass}`).toBe(value);
      }
    },
  );
}

export async function fetchHistory(
  request: APIRequestContext,
  token: string,
  assetClass?: number,
): Promise<HistoryPoint[]> {
  const filter = assetClass === undefined ? '' : `&assetClass=${assetClass}`;
  const response = await request.get(`/api/reporting/net-worth-history?range=MAX${filter}`, {
    headers: bearer(token),
  });
  expect(response.ok()).toBeTruthy();
  const body = (await response.json()) as { points: { date: string; netWorthPln: number }[] };
  return body.points.map((point) => ({ date: point.date, value: point.netWorthPln }));
}

export function between(points: HistoryPoint[], from: string, to: string): HistoryPoint[] {
  return points.filter((point) => point.date >= from && point.date <= to);
}

export function expectAllEqual(points: HistoryPoint[], value: number, label: string): void {
  expect(points.length, `${label}: points`).toBeGreaterThan(0);
  expect(
    points.map((point) => point.value),
    label,
  ).toEqual(points.map(() => value));
}
